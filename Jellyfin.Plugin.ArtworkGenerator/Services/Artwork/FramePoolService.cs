using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Artwork
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

        // The fewest episodes a season or series pool draws from. A pool asked for more frames than
        // this takes in more episodes rather than more frames per episode, so a request for N
        // candidates can return N different episodes.
        private const int MinSourcesPerPool = 3;

        // Each pass a pool makes over its sources starts every source's extraction walk this many
        // attempts further along, beyond the most a single extraction run can use, so coming back to
        // a source for more frames never revisits one.
        private const int GrowthAttemptStride = 64;

        private static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(10);

        private readonly ILogger<FramePoolService> _logger;
        private readonly FrameExtractionService _extractor;
        private readonly string _root;
        private readonly Func<int?> _fixedSeed;
        private readonly Func<int> _parallelExtractions;
        private readonly Dictionary<string, FramePool> _pools = new(StringComparer.Ordinal);
        private readonly object _sync = new();
        private bool _disposed;

        public FramePoolService(
            ILogger<FramePoolService> logger,
            FrameExtractionService extractor,
            string root,
            Func<int?> fixedSeed,
            Func<int>? parallelExtractions = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(root);
            _logger = logger;
            _extractor = extractor;
            _root = root;
            _fixedSeed = fixedSeed ?? (() => null);
            _parallelExtractions = parallelExtractions ?? (() => FrameExtractionSettings.DefaultParallelExtractions);

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
        /// <param name="sources">
        /// Finds the videos to extract from. Only called when the pool has to grow, since finding a
        /// series' episodes walks the whole series and checks each file on disk.
        /// </param>
        public async Task<FrameLease> AcquireAsync(
            string key,
            Func<IReadOnlyList<Video>> sources,
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
                    if (pool.Frames.Count < needed && !pool.Exhausted)
                    {
                        var videos = sources();
                        if (videos.Count > 0)
                        {
                            await GrowAsync(pool, videos, windowStart, windowEnd, needed, cancellationToken).ConfigureAwait(false);
                        }
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

        /// <summary>
        /// Discards every pool nobody holds, so the next request extracts afresh. Used when a
        /// setting that decides which frames are taken has changed.
        /// </summary>
        public void DiscardIdle()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                foreach (var pool in _pools.Values.Where(p => p.References == 0).ToList())
                {
                    _pools.Remove(pool.Key);
                    pool.Dispose();
                    TryDeleteDirectory(pool.Directory);
                    _logger.LogDebug("Discarded frame pool {Key}", pool.Key);
                }
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
        // A pool that never tried to extract, because nobody needed frames or the item has no
        // video, holds nothing worth keeping and would only crowd out pools that do, so it goes as
        // soon as nobody holds it.
        internal void Release(FramePool pool)
        {
            lock (_sync)
            {
                pool.References = Math.Max(0, pool.References - 1);
                pool.LastUsed = DateTime.UtcNow;

                if (!_disposed
                    && pool.References == 0
                    && pool.Rounds == 0
                    && pool.Frames.Count == 0
                    && _pools.TryGetValue(pool.Key, out var current)
                    && ReferenceEquals(current, pool))
                {
                    _pools.Remove(pool.Key);
                    pool.Dispose();
                    TryDeleteDirectory(pool.Directory);
                }
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
                    _logger.LogDebug("Created frame pool {Key} with seed {Seed}", key, seed);
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
        //
        // The pool grows several times while one item is drawn: the poster asks for its choices, then
        // the thumb asks for a few ranks more, and so on. Each round extracts only what is missing,
        // from the episodes the walk has not visited yet, so the work done for an item is in step
        // with the frames it ends up holding however many rounds that takes.
        private async Task GrowAsync(
            FramePool pool,
            IReadOnlyList<Video> sources,
            float windowStart,
            float windowEnd,
            int needed,
            CancellationToken cancellationToken)
        {
            var ordered = OrderSources(sources, pool.Seed).ToList();
            var missing = needed - pool.Frames.Count;
            var (visits, perSource) = PlanGrowth(ordered.Count, pool.NextSource, missing);
            pool.Rounds++;

            // One slot per visit, filled as each finishes. Several sources are extracted at once,
            // but the batch is read back in visit order, so the ranks come out the same however
            // the extractions happen to interleave and a fixed seed still reproduces them.
            var results = new IReadOnlyList<(ExtractedFrame Frame, string Source)>?[visits.Count];
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(_parallelExtractions(), 1, FrameExtractionSettings.MaxParallelExtractions),
                CancellationToken = cancellationToken
            };

            var batch = new List<(ExtractedFrame Frame, string Source)>();
            var visited = 0;

            try
            {
                await Parallel.ForEachAsync(Enumerable.Range(0, visits.Count), options, async (slot, token) =>
                {
                    var visit = visits[slot];
                    var source = ordered[visit % ordered.Count];
                    var frames = await _extractor.ExtractFrameCandidatesAsync(
                        source,
                        windowStart,
                        windowEnd,
                        perSource,
                        PhaseFor(pool.Seed, visit % ordered.Count),
                        (visit / ordered.Count) * GrowthAttemptStride,
                        ordered.Count > 1,
                        token).ConfigureAwait(false);

                    var sourcePath = source.Path ?? string.Empty;
                    results[slot] = frames.Select(f => (f, sourcePath)).ToArray();
                }).ConfigureAwait(false);
            }
            finally
            {
                // Whatever was extracted before a failure or cancellation is kept. A request cut off
                // by a proxy or a closed dialog would otherwise throw away every frame it had paid
                // for, and the next attempt would start again from nothing and be cut off at the
                // same point, so a large request could never finish.
                //
                // Only the unbroken run of finished visits from the start counts. The walk resumes
                // after it, so a visit that finished beyond a gap will be made again, from the same
                // timestamps, and its frames are dropped rather than held twice.
                visited = CompletedRun(results);
                for (var slot = 0; slot < results.Length; slot++)
                {
                    if (results[slot] is not { } frames)
                    {
                        continue;
                    }

                    if (slot < visited)
                    {
                        batch.AddRange(frames);
                    }
                    else
                    {
                        foreach (var (frame, _) in frames)
                        {
                            TryDeleteFile(frame.Path);
                        }
                    }
                }

                pool.NextSource += visited;
                AddToPool(pool, batch);
            }

            if (batch.Count == 0)
            {
                pool.Exhausted = true;
                _logger.LogWarning("Frame pool {Key} could not extract any more frames", pool.Key);
                return;
            }

            _logger.LogDebug(
                "Frame pool {Key} (seed {Seed}) now holds {Count} frame(s) after visiting {Sources} source(s)",
                pool.Key,
                pool.Seed,
                pool.Frames.Count,
                visited);
        }

        // PlanGrowth
        // Which steps of the source walk a growth round visits, and how many frames it asks of each.
        // Steps count on from where the last round stopped; step n is source n modulo the source
        // count, and every pass over the sources starts a step further along each one's own
        // extraction walk, so no step revisits a timestamp. The first round spreads over at least
        // MinSourcesPerPool sources so even a single image is chosen from more than one episode;
        // later rounds only top up, one source per missing frame.
        internal static (IReadOnlyList<int> Visits, int PerSource) PlanGrowth(int sourceCount, int nextSource, int missing)
        {
            if (sourceCount <= 0 || missing <= 0)
            {
                return (Array.Empty<int>(), 0);
            }

            var wanted = nextSource == 0 ? Math.Max(MinSourcesPerPool, missing) : missing;
            var take = Math.Min(sourceCount, wanted);
            var perSource = Math.Max(1, (int)Math.Ceiling(missing / (double)take));

            return (Enumerable.Range(nextSource, take).ToArray(), perSource);
        }

        // CompletedRun
        // How many slots from the start are filled before the first empty one.
        internal static int CompletedRun<T>(IReadOnlyList<T?> slots)
            where T : class
        {
            var run = 0;
            while (run < slots.Count && slots[run] != null)
            {
                run++;
            }

            return run;
        }

        // AddToPool
        // Ranks a batch and moves its frames into the pool after the frames already held.
        private void AddToPool(FramePool pool, List<(ExtractedFrame Frame, string Source)> batch)
        {
            if (batch.Count == 0)
            {
                return;
            }

            foreach (var (frame, source) in InterleaveBySource(batch, b => b.Source, b => b.Frame.Score))
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
        }

        // InterleaveBySource
        // Ranks the frames so that consecutive ranks come from different episodes wherever the batch
        // allows. Each episode's frames are ordered among themselves by score, the episodes are
        // ordered by their best frame, and then one frame is taken from each in turn. Ranking purely
        // by score let a single episode supply nearly every candidate the image picker offered,
        // because one good-looking episode tends to score well throughout.
        internal static IReadOnlyList<T> InterleaveBySource<T>(
            IReadOnlyList<T> items,
            Func<T, string> source,
            Func<T, double> score)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(score);

            var groups = items
                .GroupBy(source, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(score).ToList())
                .OrderByDescending(group => score(group[0]))
                .ToList();

            var ordered = new List<T>(items.Count);
            for (int round = 0; ordered.Count < items.Count; round++)
            {
                foreach (var group in groups)
                {
                    if (round < group.Count)
                    {
                        ordered.Add(group[round]);
                    }
                }
            }

            return ordered;
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
        internal static IEnumerable<T> OrderSources<T>(IReadOnlyList<T> sources, int seed)
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

        // The next step of the source walk a growth round starts from.
        public int NextSource { get; set; }

        public bool Exhausted { get; set; }

        public void Dispose() => Gate.Dispose();
    }

    internal readonly record struct PooledFrame(string Path, double Score, string SourcePath);
}
