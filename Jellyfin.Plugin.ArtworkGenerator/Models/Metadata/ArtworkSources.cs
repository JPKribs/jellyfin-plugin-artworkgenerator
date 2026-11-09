using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    /// <summary>
    /// Finds the episodes whose video can supply frames for an item's artwork.
    /// </summary>
    public static class ArtworkSources
    {
        /// <summary>The default for <see cref="FrameExtractionSettings.VideosPerParent"/>: 0, every video.</summary>
        public const int DefaultVideosPerParent = 0;

        /// <summary>The highest <see cref="FrameExtractionSettings.VideosPerParent"/> accepted.</summary>
        public const int MaxVideosPerParent = 2000;

        // Files checked on disk for each video kept, so a partly offline parent still fills its sample.
        internal const int ChecksPerVideo = 10;
        private const int MaxCachedContentTypes = 5000;

        private static readonly ConcurrentDictionary<Guid, CollectionType?> ContentTypes = new();

        // GetPlayableSources
        // The videos with a readable media file that belong to an item: the video itself, a
        // season's episodes, a series' episodes, or the videos a collection, folder, or library
        // holds. Specials are left out of a series' or group's pool whenever regular episodes exist,
        // since they are often recaps or extras. With Videos Per Parent above 0, a parent keeps at most
        // that many, spread across all of it, and checks at most ChecksPerVideo files on disk for
        // each one kept. A season or series keeps its sample in broadcast order. At 0 every video is used.
        public static IReadOnlyList<Video> GetPlayableSources(BaseItem item)
            => GetPlayableSources(item, Plugin.Instance?.Configuration?.FrameExtraction?.VideosPerParent ?? DefaultVideosPerParent);

        // GetPlayableSources
        // The same, keeping at most videosPerParent videos for a parent, or all of them at 0.
        public static IReadOnlyList<Video> GetPlayableSources(BaseItem item, int videosPerParent)
        {
            ArgumentNullException.ThrowIfNull(item);

            var kind = KindOf(item);
            IEnumerable<Video> videos = kind switch
            {
                ArtworkItemKind.Season => SeasonEpisodes((Season)item),
                ArtworkItemKind.Series => SafeRecursiveChildren((Series)item),
                ArtworkItemKind.Collection => CollectionVideos((BoxSet)item),
                ArtworkItemKind.Folder or ArtworkItemKind.Library => GroupVideos((Folder)item),
                null => Array.Empty<Video>(),
                _ => new[] { (Video)item }
            };

            return Choose(kind, videos, videosPerParent);
        }

        // Choose
        // Picks the readable videos an item of this kind keeps from its candidates.
        internal static IReadOnlyList<Video> Choose(ArtworkItemKind? kind, IEnumerable<Video> videos, int videosPerParent)
        {
            var candidates = videos
                .Where(v => !v.IsVirtualItem && !string.IsNullOrEmpty(v.Path))
                .OrderBy(v => v.ParentIndexNumber ?? 0)
                .ThenBy(v => v.IndexNumber ?? 0)
                .ToList();

            var limit = Math.Min(videosPerParent, MaxVideosPerParent);
            List<Video> playable;
            if (limit <= 0 || (kind?.IsGroup() != true && kind is not (ArtworkItemKind.Season or ArtworkItemKind.Series)))
            {
                playable = candidates.Where(v => File.Exists(v.Path)).ToList();
            }
            else if (kind?.IsGroup() == true)
            {
                playable = Sample(candidates, limit).ToList();
            }
            else
            {
                var kept = new HashSet<Video>(Sample(candidates, limit), ReferenceEqualityComparer.Instance);
                playable = candidates.Where(kept.Contains).ToList();
            }

            if ((kind is ArtworkItemKind.Series || kind?.IsGroup() == true)
                && playable.Any(v => (v.ParentIndexNumber ?? 1) > 0))
            {
                playable = playable.Where(v => (v.ParentIndexNumber ?? 1) > 0).ToList();
            }

            return playable;
        }

        // Sample
        // Up to limit readable videos, spread evenly across the candidates.
        private static IEnumerable<Video> Sample(IReadOnlyList<Video> candidates, int limit)
            => SpreadOrder(candidates, limit).Take(limit * ChecksPerVideo).Where(v => File.Exists(v.Path)).Take(limit);

        // MARK: KindOf
        public static ArtworkItemKind? KindOf(BaseItem item) => item switch
        {
            Episode => ArtworkItemKind.Episode,
            Season => ArtworkItemKind.Season,
            Series => ArtworkItemKind.Series,
            Movie => ArtworkItemKind.Movie,
            Video => ArtworkItemKind.Video,
            BoxSet => ArtworkItemKind.Collection,
            CollectionFolder library when CanHoldVideo(library.CollectionType) => ArtworkItemKind.Library,
            Folder folder when IsPlainFolder(folder) && CanHoldVideo(ContentTypeOf(folder)) => ArtworkItemKind.Folder,
            _ => null
        };

        // MARK: CanHoldVideo
        internal static bool CanHoldVideo(CollectionType? type) => type is not (
            CollectionType.music
            or CollectionType.books
            or CollectionType.photos
            or CollectionType.livetv
            or CollectionType.playlists);

        // MARK: ContentTypeOf
        private static CollectionType? ContentTypeOf(Folder folder)
        {
            if (folder.Id != Guid.Empty && ContentTypes.TryGetValue(folder.Id, out var cached))
            {
                return cached;
            }

            CollectionType? type;
            try
            {
                type = BaseItem.LibraryManager?.GetContentType(folder);
            }
            catch (Exception)
            {
                return null;
            }

            if (folder.Id != Guid.Empty)
            {
                if (ContentTypes.Count >= MaxCachedContentTypes)
                {
                    ContentTypes.Clear();
                }

                ContentTypes[folder.Id] = type;
            }

            return type;
        }

        // MARK: SpreadOrder
        internal static IEnumerable<T> SpreadOrder<T>(IReadOnlyList<T> items, int perPass)
        {
            var stride = Math.Max(1, (int)Math.Ceiling(items.Count / (double)Math.Max(1, perPass)));
            for (var offset = 0; offset < stride; offset++)
            {
                for (var i = offset; i < items.Count; i += stride)
                {
                    yield return items[i];
                }
            }
        }

        // MARK: GroupVideos
        private static IEnumerable<Video> GroupVideos(Folder folder)
        {
            try
            {
                return folder.GetRecursiveChildren()
                    .SelectMany(child => child switch
                    {
                        Video video => new[] { video },
                        BoxSet boxSet => CollectionVideos(boxSet),
                        _ => Array.Empty<Video>()
                    })
                    .ToList();
            }
            catch (InvalidOperationException)
            {
                return Array.Empty<Video>();
            }
        }

        // MARK: IsPlainFolder
        public static bool IsPlainFolder(Folder folder) => folder?.GetType() == typeof(Folder);

        // MARK: CollectionVideos
        private static IEnumerable<Video> CollectionVideos(BoxSet boxSet)
        {
            try
            {
                return boxSet.GetLinkedChildren()
                    .SelectMany(child => child switch
                    {
                        Video video => new[] { video },
                        Folder folder => SafeRecursiveChildren(folder),
                        _ => Array.Empty<Video>()
                    })
                    .ToList();
            }
            catch (InvalidOperationException)
            {
                return Array.Empty<Video>();
            }
        }

        // SeasonEpisodes
        // A season's own children come back empty in some libraries even when its episodes are on
        // disk and extract perfectly well on their own, which left every season poster falling back
        // to the series backdrop. So the season asks its series for the episodes and keeps the ones
        // that are its own, and only uses its own children when they are actually there.
        private static IEnumerable<Video> SeasonEpisodes(Season season)
        {
            var own = SafeChildren(season).ToList();
            if (own.Count > 0)
            {
                return own;
            }

            var series = season.Series;
            if (series == null)
            {
                return Array.Empty<Video>();
            }

            return SafeRecursiveChildren(series).Where(video => BelongsTo(video, season));
        }

        // BelongsTo
        // Matches on the season's identity where the episode carries it, and on the season number
        // otherwise, so an episode that was never linked to a season row is still placed.
        internal static bool BelongsTo(Video video, Season season)
        {
            if (video is not Episode episode)
            {
                return false;
            }

            if (episode.SeasonId != Guid.Empty)
            {
                return episode.SeasonId == season.Id;
            }

            return season.IndexNumber.HasValue && episode.ParentIndexNumber == season.IndexNumber;
        }

        private static IEnumerable<Video> SafeChildren(Folder folder)
        {
            try
            {
                return folder.Children?.OfType<Video>().ToList() ?? new List<Video>();
            }
            catch (InvalidOperationException)
            {
                return Array.Empty<Video>();
            }
        }

        private static IEnumerable<Video> SafeRecursiveChildren(Folder folder)
        {
            try
            {
                return folder.GetRecursiveChildren()?.OfType<Video>().ToList() ?? new List<Video>();
            }
            catch (InvalidOperationException)
            {
                return Array.Empty<Episode>();
            }
        }
    }
}
