using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        // GetPlayableSources
        // The episodes with a readable media file that belong to an item, in broadcast order: the
        // episode itself, a season's episodes, or a series' episodes. Specials are left out of a
        // series' pool whenever regular episodes exist, since they are often recaps or extras.
        public static IReadOnlyList<Video> GetPlayableSources(BaseItem item)
        {
            ArgumentNullException.ThrowIfNull(item);

            IEnumerable<Video> episodes = item switch
            {
                Movie movie => new[] { (Video)movie },
                Episode episode => new[] { (Video)episode },
                Season season => SeasonEpisodes(season),
                Series series => SafeRecursiveChildren(series),
                _ => Array.Empty<Episode>()
            };

            var playable = episodes
                .Where(e => !e.IsVirtualItem && !string.IsNullOrEmpty(e.Path) && File.Exists(e.Path))
                .OrderBy(e => e.ParentIndexNumber ?? 0)
                .ThenBy(e => e.IndexNumber ?? 0)
                .ToList();

            if (item is Series && playable.Any(e => (e.ParentIndexNumber ?? 1) > 0))
            {
                playable = playable.Where(e => (e.ParentIndexNumber ?? 1) > 0).ToList();
            }

            return playable;
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
