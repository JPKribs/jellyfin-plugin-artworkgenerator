using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Services.Artwork
{
    /// <summary>
    /// Shares one set of extracted frames between every image generated for the same item.
    /// </summary>
    /// <remarks>
    /// Jellyfin asks for each image type separately: a series' poster, thumb, and backdrop arrive
    /// as three calls. Extracting frames independently for each would triple the ffmpeg work and
    /// could draw them from unrelated parts of the show. Instead the first request extracts and
    /// scores a pool of frames under one seed, and every request in the next few minutes draws
    /// from that pool by rank. Once a pool has sat idle long enough it is discarded, so a later
    /// refresh starts a new pool with a new seed and still finds different frames.
    ///
    /// A fixed seed can be configured. Each pool then derives its seed from it and the item, so the
    /// same item always yields the same frames, which makes output reproducible when debugging.
    /// </remarks>
    public sealed class FramePoolService : IDisposable
    {
        private const int MaxPools = 16;
        private const int MaxSourcesPerPool = 3;

        // Each growth round starts the extraction walk this many attempts further along, beyond the
        // most a single extraction run can use, so asking for more frames never revisits one.
        private const int GrowthAttemptStride = 64;

        private static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(10);

        private readonly ILogger<FramePoolService> _logger;
        private readonly FrameExtractionService _extractor;
        private readonly string _root;
        private readonly Func<int?> _fixedSeed;
        private readonly Dictionary<string, FramePool> _pools = new(StringComparer.Ordinal);
        private readonly object _sync = new();
        private bool _disposed;

        public FramePoolService(ILogger<FramePoolService> logger, FrameExtractionService extractor, string root, Func<int?> fixedSeed)
        {
            ArgumentException.ThrowIfNullOrEmpty(root);
            _logger = logger;
            _extractor = extractor;
            _root = root;
            _fixedSeed = fixedSeed ?? (() => null);

            ResetRoot();
        }

        /// <summary>
        /// Builds the pool key for an item and extraction window. Different windows sample different
        /// parts of the video, so they cannot share a pool.
        /// </summary>
        public static string KeyFor(Guid itemId, float windowStart, float windowEnd)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{itemId:N}:{windowStart:0.##}-{windowEnd:0.##}");
        }

        /// <summary>
        /// Returns a lease on the item's frame pool, growing it until it holds at least
        /// <paramref name="needed"/> frames when the sources allow. Frames keep their rank once added,
        /// so rank 0 is the same frame for every caller of the same pool. Dispose the lease once the
        /// frames have been decoded.
        /// </summary>
        public async Task<FrameLease> AcquireAsync(
            string key,
            IReadOnlyList<Episode> sources,
            float windowStart,
            float windowEnd,
            int needed,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(sources);

            var pool = Retain(key);
            try
            {
                await pool.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (pool.Frames.Count < needed && !pool.Exhausted && sources.Count > 0)
                    {
                        await GrowAsync(pool, sources, windowStart, windowEnd, needed, cancellationToken).ConfigureAwait(false);
                    }

                    return new FrameLease(this, pool, pool.Frames.Select(f => f.Path).ToArray());
                }
                finally
                {
                    pool.Gate.Release();
                }
            }
            catch
            {
                Release(pool);
                throw;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                foreach (var pool in _pools.Values)
                {
                    pool.Dispose();
                    TryDeleteDirectory(pool.Directory);
                }

                _pools.Clear();
            }
        }

        // Release
        // Returns a pool reference taken by Retain. Idle pools are only discarded at zero references.
        internal void Release(FramePool pool)
        {
            lock (_sync)
            {
                pool.References = Math.Max(0, pool.References - 1);
                pool.LastUsed = DateTime.UtcNow;
            }
        }

        // Retain
        // Finds or creates the pool for a key and takes a reference on it, so it cannot be pruned
        // while this caller waits for or holds it.
        private FramePool Retain(string key)
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                PruneLocked();

                if (!_pools.TryGetValue(key, out var pool))
                {
                    var seed = CreateSeed(key);
                    var directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(directory);

                    pool = new FramePool(key, seed, directory);
                    _pools[key] = pool;
                    _logger.LogInformation("Created frame pool {Key} with seed {Seed}", key, seed);
                }

                pool.References++;
                pool.LastUsed = DateTime.UtcNow;
                return pool;
            }
        }

        // PruneLocked
        // Discards pools nobody holds that have sat idle past their lifetime, then the least
        // recently used idle pools while there are more than the cap. Caller holds _sync.
        private void PruneLocked()
        {
            var now = DateTime.UtcNow;
            var idle = _pools.Values.Where(p => p.References == 0).OrderBy(p => p.LastUsed).ToList();
            var discard = idle.Where(p => now - p.LastUsed > IdleLifetime).ToList();

            var overflow = _pools.Count - discard.Count - MaxPools + 1;
            if (overflow > 0)
            {
                discard.AddRange(idle.Except(discard).Take(overflow));
            }

            foreach (var pool in discard)
            {
                _pools.Remove(pool.Key);
                pool.Dispose();
                TryDeleteDirectory(pool.Directory);
                _logger.LogDebug("Discarded frame pool {Key}", pool.Key);
            }
        }

        // GrowAsync
        // Extracts more frames into a pool. Sources are drawn in a seeded order, so a season or
        // series pool samples the same episodes for the same seed. New frames are ranked among
        // themselves by score and appended after the frames already held.
        private async Task GrowAsync(
            FramePool pool,
            IReadOnlyList<Episode> sources,
            float windowStart,
            float windowEnd,
            int needed,
            CancellationToken cancellationToken)
        {
            var chosen = OrderSources(sources, pool.Seed).Take(MaxSourcesPerPool).ToList();
            var missing = needed - pool.Frames.Count;
            var perSource = Math.Max(1, (int)Math.Ceiling(missing / (double)chosen.Count));
            var attemptOffset = pool.Rounds * GrowthAttemptStride;
            pool.Rounds++;

            var batch = new List<(ExtractedFrame Frame, string Source)>();

            try
            {
                for (int i = 0; i < chosen.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var frames = await _extractor.ExtractFrameCandidatesAsync(
                        chosen[i],
                        windowStart,
                        windowEnd,
                        perSource,
                        PhaseFor(pool.Seed, i),
                        attemptOffset,
                        cancellationToken).ConfigureAwait(false);

                    var sourcePath = chosen[i].Path ?? string.Empty;
                    batch.AddRange(frames.Select(f => (f, sourcePath)));
                }
            }
            catch
            {
                // Frames extracted before a failure or cancellation must not be stranded on disk.
                foreach (var (frame, _) in batch)
                {
                    TryDeleteFile(frame.Path);
                }

                throw;
            }

            if (batch.Count == 0)
            {
                pool.Exhausted = true;
                _logger.LogWarning("Frame pool {Key} could not extract any more frames", pool.Key);
                return;
            }

            foreach (var (frame, source) in batch.OrderByDescending(b => b.Frame.Score))
            {
                var destination = Path.Combine(
                    pool.Directory,
                    string.Create(CultureInfo.InvariantCulture, $"{pool.Frames.Count:D3}{Path.GetExtension(frame.Path)}"));

                try
                {
                    File.Move(frame.Path, destination, overwrite: true);
                    pool.Frames.Add(new PooledFrame(destination, frame.Score, source));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Could not move an extracted frame into pool {Key}", pool.Key);
                    TryDeleteFile(frame.Path);
                }
            }

            _logger.LogInformation(
                "Frame pool {Key} (seed {Seed}) now holds {Count} frame(s) from {Sources} source(s)",
                pool.Key,
                pool.Seed,
                pool.Frames.Count,
                chosen.Count);
        }

        // CreateSeed
        // A fresh random seed, or one derived from the configured fixed seed and the pool key so
        // the same item reproduces the same frames.
        private int CreateSeed(string key)
        {
            var fixedSeed = _fixedSeed();
            return fixedSeed.HasValue
                ? unchecked(StableHash(key) ^ fixedSeed.Value)
                : Random.Shared.Next();
        }

        // OrderSources
        // Shuffles the sources deterministically for a seed.
        internal static IEnumerable<Episode> OrderSources(IReadOnlyList<Episode> sources, int seed)
        {
            var random = new Random(seed);
            return sources
                .Select(source => (Source: source, Key: random.Next()))
                .OrderBy(entry => entry.Key)
                .Select(entry => entry.Source);
        }

        // PhaseFor
        // The starting point of the extraction walk for the nth source of a pool, in [0, 1).
        internal static double PhaseFor(int seed, int sourceIndex)
        {
            return new Random(unchecked((seed * 397) ^ sourceIndex)).NextDouble();
        }

        // StableHash
        // FNV-1a over the key. string.GetHashCode is randomized per process, which would break
        // reproducibility across server restarts.
        internal static int StableHash(string value)
        {
            unchecked
            {
                int hash = (int)2166136261;
                foreach (char c in value)
                {
                    hash ^= c;
                    hash *= 16777619;
                }

                return hash;
            }
        }

        private void ResetRoot()
        {
            // Pools do not survive a restart, so anything left on disk is from a previous run.
            TryDeleteDirectory(_root);
            Directory.CreateDirectory(_root);
        }

        private void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not delete frame pool directory {Path}", path);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// A hold on a frame pool's current frames, best first. Dispose it once the frames are decoded.
    /// </summary>
    public sealed class FrameLease : IDisposable
    {
        private readonly FramePool _pool;
        private FramePoolService? _owner;

        internal FrameLease(FramePoolService owner, FramePool pool, IReadOnlyList<string> paths)
        {
            _owner = owner;
            _pool = pool;
            Paths = paths;
        }

        /// <summary>Gets the frame files in rank order.</summary>
        public IReadOnlyList<string> Paths { get; }

        /// <summary>Gets the seed the pool was created with.</summary>
        public int Seed => _pool.Seed;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Release(_pool);
        }
    }

    internal sealed class FramePool : IDisposable
    {
        public FramePool(string key, int seed, string directory)
        {
            Key = key;
            Seed = seed;
            Directory = directory;
        }

        public string Key { get; }

        public int Seed { get; }

        public string Directory { get; }

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public List<PooledFrame> Frames { get; } = new();

        public int References { get; set; }

        public DateTime LastUsed { get; set; } = DateTime.UtcNow;

        public int Rounds { get; set; }

        public bool Exhausted { get; set; }

        public void Dispose() => Gate.Dispose();
    }

    internal readonly record struct PooledFrame(string Path, double Score, string SourcePath);
}
