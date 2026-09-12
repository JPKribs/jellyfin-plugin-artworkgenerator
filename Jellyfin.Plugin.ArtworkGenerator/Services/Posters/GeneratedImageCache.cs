using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Posters
{
    /// <summary>
    /// Short-lived store for posters rendered on demand for Jellyfin's Edit Images picker.
    /// </summary>
    /// <remarks>
    /// The picker addresses candidate images by URL, and both the thumbnail request and the
    /// eventual download are made by the server's own HTTP client, which carries no user
    /// credentials. Rather than expose an unauthenticated endpoint that would render a poster
    /// on demand — an easy way to make a stranger burn ffmpeg time — generation happens up
    /// front inside the authenticated provider call and the result is parked here under an
    /// unguessable token. The public endpoint then only ever performs a dictionary lookup.
    /// Entries are held in memory because they are read within seconds and never reused.
    /// </remarks>
    public class GeneratedImageCache
    {
        /// <summary>
        /// Maximum number of rendered posters held at once. Ten candidates per episode means
        /// this covers several concurrently open picker dialogs before the oldest are dropped.
        /// </summary>
        private const int MaxEntries = 64;

        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

        private readonly ILogger<GeneratedImageCache> _logger;
        private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);

        public GeneratedImageCache(ILogger<GeneratedImageCache> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Stores an encoded image and returns the opaque token addressing it.
        /// </summary>
        public string Add(byte[] imageBytes, string contentType = "image/jpeg")
        {
            ArgumentNullException.ThrowIfNull(imageBytes);

            Prune();

            var token = Guid.NewGuid().ToString("N");
            _entries[token] = new CacheEntry(imageBytes, contentType, DateTimeOffset.UtcNow);
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
                _entries.TryRemove(token, out _);
                return false;
            }

            imageBytes = entry.Bytes;
            contentType = entry.ContentType;
            return true;
        }

        // Prune
        // Drops expired entries, then the oldest surviving ones if the cache is still full.
        private void Prune()
        {
            foreach (var pair in _entries)
            {
                if (IsExpired(pair.Value))
                {
                    _entries.TryRemove(pair.Key, out _);
                }
            }

            var overflow = _entries.Count - MaxEntries + 1;
            if (overflow <= 0)
            {
                return;
            }

            var oldest = _entries
                .OrderBy(p => p.Value.CreatedAt)
                .Take(overflow)
                .Select(p => p.Key)
                .ToArray();

            foreach (var key in oldest)
            {
                _entries.TryRemove(key, out _);
            }

            _logger.LogDebug("Evicted {Count} generated image(s) to stay within the cache bound", oldest.Length);
        }

        private static bool IsExpired(CacheEntry entry)
            => DateTimeOffset.UtcNow - entry.CreatedAt > Lifetime;

        private sealed record CacheEntry(byte[] Bytes, string ContentType, DateTimeOffset CreatedAt);
    }
}
