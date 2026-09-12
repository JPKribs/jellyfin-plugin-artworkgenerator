using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Posters
{
    /// <summary>
    /// Remembers the choices last rendered for an item, so Edit Images does not render every image
    /// type again each time the dialog asks.
    /// </summary>
    /// <remarks>
    /// Jellyfin asks a remote provider for all of its images at once and filters them to the type
    /// the dialog is showing afterwards, and the dialog asks again on every type switch, page, and
    /// language toggle. Without this, each of those rendered every poster, thumb, logo, and backdrop
    /// the item's profile fills. A remembered set is reused only while nothing it was drawn from has
    /// changed, meaning the configuration and the images of the item, its season, and its series,
    /// and only while every image in it can still be downloaded for a while longer.
    /// </remarks>
    public sealed class GeneratedCandidateMemo
    {
        // A choice offered again must stay downloadable long enough for the user to pick it.
        private static readonly TimeSpan PickMargin = TimeSpan.FromMinutes(2);

        // Items remembered at once. Each entry is only a list of URLs; the images live in the cache.
        private const int MaxItems = 32;

        private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
        private long _sequence;

        /// <summary>
        /// Builds the value a remembered set must match to be reused: the configuration version, the
        /// language the choices are tagged with, and every image of the item, its season, and its
        /// series, since the logo, the series poster, and the series backdrop can all appear in a
        /// render.
        /// </summary>
        public static string StampFor(BaseItem item, int configurationVersion)
        {
            ArgumentNullException.ThrowIfNull(item);

            var stamp = new StringBuilder()
                .Append(configurationVersion.ToString(CultureInfo.InvariantCulture))
                .Append('|')
                .Append(item.PreferredMetadataLanguage);

            var owners = item switch
            {
                Episode episode => new BaseItem?[] { episode, episode.Season, episode.Series },
                Season season => new BaseItem?[] { season, season.Series },
                _ => new BaseItem?[] { item }
            };

            foreach (var owner in owners)
            {
                if (owner?.ImageInfos == null)
                {
                    continue;
                }

                foreach (var image in owner.ImageInfos)
                {
                    stamp.Append('|')
                        .Append(image.Type.ToString())
                        .Append(':')
                        .Append(image.Path)
                        .Append(':')
                        .Append(image.DateModified.Ticks.ToString(CultureInfo.InvariantCulture));
                }
            }

            return stamp.ToString();
        }

        /// <summary>
        /// Returns the choices remembered for an item when they match <paramref name="stamp"/> and
        /// every one of them is still downloadable. A set that fails either test is forgotten.
        /// </summary>
        public bool TryGet(Guid itemId, string stamp, GeneratedImageCache cache, out IReadOnlyList<RemoteImageInfo> images)
        {
            ArgumentNullException.ThrowIfNull(cache);

            images = Array.Empty<RemoteImageInfo>();
            if (!_entries.TryGetValue(itemId, out var entry))
            {
                return false;
            }

            if (!string.Equals(entry.Stamp, stamp, StringComparison.Ordinal)
                || !entry.Tokens.All(token => cache.IsAlive(token, PickMargin)))
            {
                _entries.TryRemove(itemId, out _);
                return false;
            }

            images = entry.Images;
            return true;
        }

        /// <summary>
        /// Remembers the choices rendered for an item, along with the cache tokens they point at. An
        /// empty set is not remembered, so an item that produced nothing is tried again next time.
        /// </summary>
        public void Set(Guid itemId, string stamp, IReadOnlyList<RemoteImageInfo> images, IReadOnlyList<string> tokens)
        {
            ArgumentNullException.ThrowIfNull(images);
            ArgumentNullException.ThrowIfNull(tokens);

            if (images.Count == 0)
            {
                return;
            }

            _entries[itemId] = new Entry(stamp, images, tokens, Interlocked.Increment(ref _sequence));

            var overflow = _entries.Count - MaxItems;
            if (overflow <= 0)
            {
                return;
            }

            foreach (var key in _entries.OrderBy(p => p.Value.Sequence).Take(overflow).Select(p => p.Key).ToArray())
            {
                _entries.TryRemove(key, out _);
            }
        }

        private sealed record Entry(string Stamp, IReadOnlyList<RemoteImageInfo> Images, IReadOnlyList<string> Tokens, long Sequence);
    }
}
