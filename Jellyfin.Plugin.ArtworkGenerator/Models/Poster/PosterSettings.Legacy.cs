using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    /// <summary>
    /// TODO (12.0.2.1): delete this file alongside PosterConfigurationService.Migration.cs.
    ///
    /// The settings' former names, from when a design spoke of a title and an episode rather than a
    /// primary and a secondary line. They exist only so configurations and templates written before
    /// the rename still load: each one is null on anything saved since, and
    /// <see cref="Services.Posters.PosterConfigurationService"/> copies a non-null one onto its
    /// replacement and clears it. Nothing else reads them.
    /// </summary>
    public partial class PosterSettings
    {
        /// <summary>
        /// How a framed poster used to fill its two border edges, before the title's placement
        /// became the ordinary <see cref="TextPosition"/> every other design uses.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TextEdge? TextEdge { get; set; }

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
        /// <summary>
        /// Former boolean for whether a poster extracted a frame, before the choice became
        /// <see cref="CanvasSource"/>. Null on anything saved since.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? ExtractPoster { get; set; }

        /// <summary>
        /// Former per-axis graphic width, from when the two axes were set independently and could
        /// stretch the graphic. The larger of the two became <see cref="GraphicSize"/>.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public float? GraphicWidth { get; set; }

        /// <summary>Former per-axis graphic height. See <see cref="GraphicWidth"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public float? GraphicHeight { get; set; }
    }
}
