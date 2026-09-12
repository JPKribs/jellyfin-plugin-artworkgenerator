using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ArtworkGenerator.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        // PluginConfiguration
        // Initializes the plugin configuration with default values.
        public PluginConfiguration()
        {
            PosterConfigurations = new List<PosterConfiguration>();
            Profiles = new List<ArtworkProfile>();
            FrameExtraction = new FrameExtractionSettings();
        }

        /// <summary>
        /// Gets or sets how frames are pulled from a video and cleaned up. One set for the server,
        /// used for every poster and every backdrop, rather than a copy on each design and profile.
        /// </summary>
        public FrameExtractionSettings FrameExtraction { get; set; }

        /// <summary>
        /// Gets or sets the number of alternates offered when replacing an image from the Edit Images
        /// dialog (1-10). Items with no image of that type are only ever offered one, since that
        /// request comes from an automatic refresh that keeps a single image.
        /// </summary>
        public int ImageChoiceCount { get; set; } = 3;

        /// <summary>
        /// Gets or sets how many minutes images rendered for the Edit Images dialog stay available
        /// (5-1440). The dialog loads each choice by URL, so a choice picked after this has passed
        /// can no longer be downloaded and the dialog has to be reopened.
        /// </summary>
        public int ImageCacheMinutes { get; set; } = 30;

        /// <summary>
        /// Gets or sets a fixed seed for frame selection. When set, the same item always yields the
        /// same frames, which makes output reproducible. When empty, every refresh picks new frames.
        /// </summary>
        public int? FixedExtractionSeed { get; set; }

        /// <summary>
        /// Gets or sets the poster designs: how a portrait or landscape image looks.
        /// </summary>
        [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "List<T> required for XML serialization")]
        [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Setter required for XML serialization")]
        public List<PosterConfiguration> PosterConfigurations { get; set; }

        /// <summary>
        /// Gets or sets the profiles: which design fills each image slot, and which series use them.
        /// </summary>
        [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "List<T> required for XML serialization")]
        [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Setter required for XML serialization")]
        public List<ArtworkProfile> Profiles { get; set; }
    }
}
