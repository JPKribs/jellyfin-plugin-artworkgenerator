using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        // PluginConfiguration
        // Initializes the plugin configuration with default values.
        public PluginConfiguration()
        {
            PosterConfigurations = new List<PosterConfiguration>();
            LogoConfigurations = new List<LogoConfiguration>();
            Profiles = new List<ArtworkProfile>();
        }

        /// <summary>
        /// Gets or sets a value indicating whether artwork is generated at all. When on, items missing
        /// an image their profile fills get one during a metadata refresh, and generated images are
        /// offered in Edit Images. Turning it off disables both providers.
        /// </summary>
        public bool EnableProvider { get; set; } = true;

        /// <summary>
        /// Gets or sets the number of alternates offered when replacing an image from the Edit Images
        /// dialog (1-10). Items with no image of that type are only ever offered one, since that
        /// request comes from an automatic refresh that keeps a single image.
        /// </summary>
        public int ImageChoiceCount { get; set; } = 3;

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
        /// Gets or sets the logo designs.
        /// </summary>
        [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "List<T> required for XML serialization")]
        [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Setter required for XML serialization")]
        public List<LogoConfiguration> LogoConfigurations { get; set; }

        /// <summary>
        /// Gets or sets the profiles: which design fills each image slot, and which series use them.
        /// </summary>
        [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "List<T> required for XML serialization")]
        [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Setter required for XML serialization")]
        public List<ArtworkProfile> Profiles { get; set; }
    }
}
