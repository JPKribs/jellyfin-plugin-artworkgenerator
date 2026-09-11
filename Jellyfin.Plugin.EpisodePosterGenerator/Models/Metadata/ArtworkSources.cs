using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Models
{
    /// <summary>
    /// Finds the episodes whose video can supply frames for an item's artwork.
    /// </summary>
    public static class ArtworkSources
    {
        // GetPlayableEpisodes
        // The episodes with a readable media file that belong to an item, in broadcast order: the
        // episode itself, a season's episodes, or a series' episodes. Specials are left out of a
        // series' pool whenever regular episodes exist, since they are often recaps or extras.
        public static IReadOnlyList<Episode> GetPlayableEpisodes(BaseItem item)
        {
            ArgumentNullException.ThrowIfNull(item);

            IEnumerable<Episode> episodes = item switch
            {
                Episode episode => new[] { episode },
                Season season => SafeChildren(season),
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

        private static IEnumerable<Episode> SafeChildren(Folder folder)
        {
            try
            {
                return folder.Children?.OfType<Episode>().ToList() ?? new List<Episode>();
            }
            catch (InvalidOperationException)
            {
                return Array.Empty<Episode>();
            }
        }

        private static IEnumerable<Episode> SafeRecursiveChildren(Folder folder)
        {
            try
            {
                return folder.GetRecursiveChildren()?.OfType<Episode>().ToList() ?? new List<Episode>();
            }
            catch (InvalidOperationException)
            {
                return Array.Empty<Episode>();
            }
        }
    }
}
