using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Models
{
    /// <summary>
    /// How a text logo is generated from a series name.
    /// </summary>
    public class LogoSettings
    {
        /// <summary>Gets or sets where the logo text comes from.</summary>
        public LogoTitleSource TitleSource { get; set; } = LogoTitleSource.Title;

        /// <summary>Gets or sets a value indicating whether a year such as (2019) is removed.</summary>
        public bool StripYear { get; set; } = true;

        /// <summary>Gets or sets how a name with a subtitle, such as "Star Wars: Andor", is laid out.</summary>
        public LogoSubtitleMode SubtitleMode { get; set; } = LogoSubtitleMode.Keep;

        /// <summary>
        /// Gets or sets the small line's size as a percent of the large line, when the subtitle mode
        /// draws the title and subtitle at two sizes.
        /// </summary>
        public float SecondarySize { get; set; } = 45.0f;

        /// <summary>Gets or sets an optional regular expression whose matches are removed from the text.</summary>
        public string CustomRegex { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether the text is drawn in capitals.</summary>
        public bool Uppercase { get; set; }

        public string FontFamily { get; set; } = "Arial";

        public string FontStyle { get; set; } = "Bold";

        public bool UseCustomFont { get; set; }

        public string FontPath { get; set; } = string.Empty;

        /// <summary>Gets or sets what fills the letters: a colour, or a frame from the series.</summary>
        public LogoFill Fill { get; set; } = LogoFill.Color;

        /// <summary>Gets or sets where the text colour comes from, when the fill is a colour.</summary>
        public LogoColorSource ColorSource { get; set; } = LogoColorSource.Fixed;

        /// <summary>Gets or sets the ARGB text colour, and the fallback when sampling fails.</summary>
        public string Color { get; set; } = "#FFFFFFFF";

        public bool OutlineEnabled { get; set; }

        public string OutlineColor { get; set; } = "#FF000000";

        /// <summary>Gets or sets the outline width as a percent of the font size.</summary>
        public float OutlineWidth { get; set; } = 4.0f;

        public bool ShadowEnabled { get; set; }

        /// <summary>Gets or sets the most lines the name may wrap onto, 1 or 2.</summary>
        public int MaxLines { get; set; } = 2;

        /// <summary>Gets or sets the canvas width. 800 by 310 matches the common HD clear logo size.</summary>
        public int Width { get; set; } = 800;

        /// <summary>Gets or sets the canvas height.</summary>
        public int Height { get; set; } = 310;

        /// <summary>Gets the font file to use, or null when the custom font is off.</summary>
        [JsonIgnore]
        public string? EffectiveFontPath => UseCustomFont ? FontPath : null;

        /// <summary>Creates a shallow copy for render-time adjustments.</summary>
        public LogoSettings Clone() => (LogoSettings)MemberwiseClone();
    }
}
