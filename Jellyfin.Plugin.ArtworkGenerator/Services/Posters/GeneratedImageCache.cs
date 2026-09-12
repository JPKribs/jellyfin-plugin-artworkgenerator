using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Posters
{
    /// <summary>
    /// Short-lived store for images rendered on demand for Jellyfin's Edit Images picker.
    /// </summary>
    /// <remarks>
    /// The picker addresses candidate images by URL, and both the thumbnail request and the
    /// eventual download are made by the server's own HTTP client, which carries no user
    /// credentials. Rather than expose an unauthenticated endpoint that would render a poster
    /// on demand, an easy way to make a stranger burn ffmpeg time, generation happens up
    /// front inside the authenticated provider call and the result is parked here under an
    /// unguessable token. The public endpoint then only ever performs a dictionary lookup.
    /// </remarks>
    public sealed class GeneratedImageCache : IDisposable
    {
        /// <summary>
        /// The most bytes held at once. Images are rendered at the video's own resolution, so a
        /// picker for a 4K item can hold well over a hundred megabytes; the bound is on size
        /// rather than on a count of images of unknown size.
        /// </summary>
        public const long DefaultMaxBytes = 256L * 1024 * 1024;

        /// <summary>The lifetime used when the configured one is missing.</summary>
        public const int DefaultLifetimeMinutes = 30;

        /// <summary>The shortest an image is kept, so a choice outlasts a dialog left open for a moment.</summary>
        public const int MinLifetimeMinutes = 5;

        /// <summary>The longest an image is kept: one day.</summary>
        public const int MaxLifetimeMinutes = 1440;

        private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

        private readonly ILogger<GeneratedImageCache> _logger;
        private readonly Func<int> _lifetimeMinutes;
        private readonly TimeProvider _time;
        private readonly long _maxBytes;
        private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
        private readonly object _addLock = new();
        private readonly ITimer _sweep;
        private long _bytes;

        public GeneratedImageCache(
            ILogger<GeneratedImageCache> logger,
            Func<int>? lifetimeMinutes = null,
            long maxBytes = DefaultMaxBytes,
            TimeProvider? timeProvider = null)
        {
            _logger = logger;
            _lifetimeMinutes = lifetimeMinutes ?? (() => DefaultLifetimeMinutes);
            _maxBytes = maxBytes;
            _time = timeProvider ?? TimeProvider.System;

            // Expired images are released on a timer as well as when the next one is added, so a
            // closed dialog does not leave its images in memory until someone opens another.
            _sweep = _time.CreateTimer(_ => RemoveExpired(), null, SweepInterval, SweepInterval);
        }

        /// <summary>Gets the total size of the images currently held.</summary>
        public long Bytes => Interlocked.Read(ref _bytes);

        // Lifetime
        // Read on every check, so a changed setting applies to images already in the cache.
        private TimeSpan Lifetime
            => TimeSpan.FromMinutes(Math.Clamp(_lifetimeMinutes(), MinLifetimeMinutes, MaxLifetimeMinutes));

        /// <summary>
        /// Stores an encoded image and returns the opaque token addressing it. The oldest images are
        /// dropped once the total passes the size bound, never the one just added.
        /// </summary>
        public string Add(byte[] imageBytes, string contentType = "image/jpeg")
        {
            ArgumentNullException.ThrowIfNull(imageBytes);

            var token = Guid.NewGuid().ToString("N");

            lock (_addLock)
            {
                RemoveExpired();

                _entries[token] = new CacheEntry(imageBytes, contentType, _time.GetUtcNow());
                Interlocked.Add(ref _bytes, imageBytes.LongLength);

                EvictOverBudget(token);
            }

            return token;
        }

        /// <summary>
        /// Looks up a stored image. Returns false for unknown or expired tokens.
        /// </summary>
        public bool TryGet(string token, out byte[] imageBytes) => TryGet(token, out imageBytes, out _);

        /// <summary>
        /// Looks up a stored image and its content type. Returns false for unknown or expired tokens.
        /// </summary>
        public bool TryGet(string token, out byte[] imageBytes, out string contentType)
        {
            imageBytes = Array.Empty<byte>();
            contentType = "image/jpeg";

            if (string.IsNullOrEmpty(token) || !_entries.TryGetValue(token, out var entry))
            {
                return false;
            }

            if (IsExpired(entry))
            {
                Remove(token);
                return false;
            }

            imageBytes = entry.Bytes;
            contentType = entry.ContentType;
            return true;
        }

        /// <summary>
        /// Returns true when a token still resolves and will go on resolving for at least
        /// <paramref name="margin"/>, so a list of choices offered again cannot expire before one is
        /// picked from it.
        /// </summary>
        public bool IsAlive(string token, TimeSpan margin)
        {
            return !string.IsNullOrEmpty(token)
                && _entries.TryGetValue(token, out var entry)
                && _time.GetUtcNow() - entry.CreatedAt + margin <= Lifetime;
        }

        public void Dispose() => _sweep.Dispose();

        // RemoveExpired
        // Drops every image past its lifetime. Safe to run alongside lookups and additions.
        private void RemoveExpired()
        {
            foreach (var pair in _entries)
            {
                if (IsExpired(pair.Value))
                {
                    Remove(pair.Key);
                }
            }
        }

        // EvictOverBudget
        // Drops the oldest images until the total fits the bound, never the one just added, so an
        // image larger than the whole bound is still served. Caller holds _addLock.
        private void EvictOverBudget(string keep)
        {
            if (Bytes <= _maxBytes)
            {
                return;
            }

            var oldest = _entries
                .Where(p => !string.Equals(p.Key, keep, StringComparison.Ordinal))
                .OrderBy(p => p.Value.CreatedAt)
                .Select(p => p.Key)
                .ToArray();

            var evicted = 0;
            foreach (var key in oldest)
            {
                if (Bytes <= _maxBytes)
                {
                    break;
                }

                if (Remove(key))
                {
                    evicted++;
                }
            }

            _logger.LogDebug("Evicted {Count} generated image(s) to stay within the cache bound", evicted);
        }

        private bool Remove(string token)
        {
            if (!_entries.TryRemove(token, out var entry))
            {
                return false;
            }

            Interlocked.Add(ref _bytes, -entry.Bytes.LongLength);
            return true;
        }

        private bool IsExpired(CacheEntry entry)
            => _time.GetUtcNow() - entry.CreatedAt > Lifetime;

        private sealed record CacheEntry(byte[] Bytes, string ContentType, DateTimeOffset CreatedAt);
    }
}
