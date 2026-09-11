using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Models
{
    /// <summary>
    /// The files and video geometry an artwork render draws on.
    /// </summary>
    public class VideoMetadata
    {
        private const int DefaultWidth = 1920;
        private const int DefaultHeight = 1080;

        public string? SeriesLogoFilePath { get; set; }

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
        // Builds the metadata for an item: series artwork paths, and the geometry of the episode
        // that will supply frames, when there is one.
        public static VideoMetadata Create(BaseItem item, Series? series, Episode? source)
        {
            var metadata = new VideoMetadata
            {
                SourcePath = source?.Path ?? item?.Path
            };

            if (series != null)
            {
                metadata.SeriesLogoFilePath = series.GetImages(ImageType.Logo).FirstOrDefault()?.Path;
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
    }
}
