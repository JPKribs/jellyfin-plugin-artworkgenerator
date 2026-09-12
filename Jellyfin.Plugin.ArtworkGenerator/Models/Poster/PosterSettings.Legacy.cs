using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    /// <summary>
    /// The settings' former names, from when a design spoke of a title and an episode rather than a
    /// primary and a secondary line. They exist only so configurations and templates written before
    /// the rename still load: each one is null on anything saved since, and
    /// <see cref="Services.Posters.PosterConfigurationService"/> copies a non-null one onto its
    /// replacement and clears it. Nothing else reads them.
    /// </summary>
    public partial class PosterSettings
    {
        /// <summary>Former name of <see cref="ShowPrimary"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? ShowTitle { get; set; }

        /// <summary>Former name of <see cref="ShowSecondary"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? ShowEpisode { get; set; }

        /// <summary>Former name of <see cref="PrimaryUseCustomFont"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? TitleUseCustomFont { get; set; }

        /// <summary>Former name of <see cref="SecondaryUseCustomFont"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? EpisodeUseCustomFont { get; set; }

        /// <summary>Former name of <see cref="PrimaryFontFamily"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TitleFontFamily { get; set; }

        /// <summary>Former name of <see cref="PrimaryFontPath"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TitleFontPath { get; set; }

        /// <summary>Former name of <see cref="PrimaryFontStyle"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TitleFontStyle { get; set; }

        /// <summary>Former name of <see cref="PrimaryFontSize"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public float? TitleFontSize { get; set; }

        /// <summary>Former name of <see cref="PrimaryFontColor"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TitleFontColor { get; set; }

        /// <summary>Former name of <see cref="SecondaryFontFamily"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? EpisodeFontFamily { get; set; }

        /// <summary>Former name of <see cref="SecondaryFontPath"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? EpisodeFontPath { get; set; }

        /// <summary>Former name of <see cref="SecondaryFontStyle"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? EpisodeFontStyle { get; set; }

        /// <summary>Former name of <see cref="SecondaryFontSize"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public float? EpisodeFontSize { get; set; }

        /// <summary>Former name of <see cref="SecondaryFontColor"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? EpisodeFontColor { get; set; }

        /// <summary>Former name of <see cref="TextEdge"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TextEdge? TitleEdge { get; set; }

        /// <summary>Former name of <see cref="LongTextHandling"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public LongTextHandling? LongTitleHandling { get; set; }
    }
}
