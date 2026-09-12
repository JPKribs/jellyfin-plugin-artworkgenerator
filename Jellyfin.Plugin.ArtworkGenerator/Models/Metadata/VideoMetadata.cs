using System.IO;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    /// <summary>
    /// The files and video geometry an artwork render draws on.
    /// </summary>
    public class VideoMetadata
    {
        private const int DefaultWidth = 1920;
        private const int DefaultHeight = 1080;

        /// <summary>
        /// Gets or sets the logo set on the item, or failing that on its season or series. Only a
        /// logo actually selected for the item counts; nothing is rendered in its place.
        /// </summary>
        public string? LogoFilePath { get; set; }

        public string? SeriesPosterFilePath { get; set; }

        public string? SeriesBackdropFilePath { get; set; }

        /// <summary>
        /// Gets or sets the path that identifies the render for seeded randomness, such as the Brush
        /// stroke layout: the episode's file, or the season or series folder.
        /// </summary>
        public string? SourcePath { get; set; }

        public int VideoWidth { get; set; } = DefaultWidth;

        public int VideoHeight { get; set; } = DefaultHeight;

        public long VideoLengthTicks { get; set; }

        // Create
        // Builds the metadata for an item: its logo and series artwork paths, and the geometry of
        // the episode that will supply frames, when there is one.
        public static VideoMetadata Create(BaseItem item, Series? series, Video? source)
        {
            var metadata = new VideoMetadata
            {
                SourcePath = source?.Path ?? item?.Path
            };

            metadata.LogoFilePath = FindLogo(item, series);

            if (series != null)
            {
                metadata.SeriesPosterFilePath = series.GetImages(ImageType.Primary).FirstOrDefault()?.Path;
                metadata.SeriesBackdropFilePath = series.GetImages(ImageType.Backdrop).FirstOrDefault()?.Path;
            }

            if (source != null)
            {
                var videoStream = source.GetMediaStreams()?.FirstOrDefault(s => s.Type == MediaStreamType.Video);
                metadata.VideoWidth = videoStream?.Width ?? DefaultWidth;
                metadata.VideoHeight = videoStream?.Height ?? DefaultHeight;
                metadata.VideoLengthTicks = source.RunTimeTicks ?? 0;
            }

            return metadata;
        }

        // FindLogo
        // The logo selected for the item itself, then for its season, then for its series. A film
        // has no series, so its own logo is the only one it can draw. A logo Jellyfin only knows by
        // its web address, or whose file is gone, is passed over for the next one that can be read.
        private static string? FindLogo(BaseItem? item, Series? series)
        {
            var owners = new[] { item, (item as Episode)?.Season, series };
            return owners
                .Where(owner => owner != null)
                .SelectMany(owner => owner!.GetImages(ImageType.Logo))
                .Where(image => image.IsLocalFile && File.Exists(image.Path))
                .Select(image => image.Path)
                .FirstOrDefault();
        }
    }
}
