using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    /// <summary>
    /// How a text logo is generated from a series name.
    /// </summary>
    public class LogoSettings
    {
        /// <summary>Gets or sets where the logo text comes from.</summary>
        [Display(Name = "Logo Source", Description = "Which name the logo shows.")]
        public LogoTitleSource TitleSource { get; set; } = LogoTitleSource.Title;

        /// <summary>Gets or sets a value indicating whether a year such as (2019) is removed.</summary>
        [Display(Name = "Remove Year", Description = "Remove a year in brackets, such as (2019).")]
        public bool StripYear { get; set; } = true;

        /// <summary>Gets or sets how a name with a subtitle, such as "Star Wars: Andor", is laid out.</summary>
        [Display(Name = "Names With a Subtitle", Description = "How to treat a name split by a colon or dash.")]
        public LogoSubtitleMode SubtitleMode { get; set; } = LogoSubtitleMode.Keep;

        /// <summary>
        /// Gets or sets the small line's size as a percent of the large line, when the subtitle mode
        /// draws the title and subtitle at two sizes.
        /// </summary>
        [Display(Name = "Small Line Size (%)", Description = "The small line's size as a percent of the large one.")]
        public float SecondarySize { get; set; } = 45.0f;

        /// <summary>Gets or sets an optional regular expression whose matches are removed from the text.</summary>
        [Display(Name = "Remove Pattern", Description = "Anything matching this regular expression is removed.")]
        public string CustomRegex { get; set; } = string.Empty;

        /// <summary>Gets or sets how the letters are cased, whatever case the name arrives in.</summary>
        [Display(Name = "Letter Case", Description = "How the letters are cased.")]
        public LogoCase LetterCase { get; set; }

        [Display(Name = "Font")]
        public string FontFamily { get; set; } = "Arial";

        [Display(Name = "Font Style")]
        public string FontStyle { get; set; } = "Bold";

        [Display(Name = "Use Custom Font", Description = "Use a font file instead of an installed font.")]
        public bool UseCustomFont { get; set; }

        [Display(Name = "Font Path", Description = "Path to a TTF, OTF, or TTC font file readable by the server.")]
        public string FontPath { get; set; } = string.Empty;

        /// <summary>Gets or sets what fills the letters: a color, or a frame from the series.</summary>
        [Display(Name = "Letters Filled With", Description = "What the letters are filled with.")]
        public LogoFill Fill { get; set; } = LogoFill.Color;

        /// <summary>Gets or sets where the text color comes from, when the fill is a color.</summary>
        [Display(Name = "Color From", Description = "Where the letter color comes from.")]
        public LogoColorSource ColorSource { get; set; } = LogoColorSource.Fixed;

        /// <summary>Gets or sets the ARGB text color, and the fallback when sampling fails.</summary>
        [Display(Name = "Text Color")]
        public string Color { get; set; } = "#FFFFFFFF";

        [Display(Name = "Outline")]
        public bool OutlineEnabled { get; set; }

        [Display(Name = "Outline Color")]
        public string OutlineColor { get; set; } = "#FF000000";

        /// <summary>Gets or sets the outline width as a percent of the font size.</summary>
        [Display(Name = "Outline Width (%)", Description = "Stroke width as a percent of the font size.")]
        public float OutlineWidth { get; set; } = 4.0f;

        [Display(Name = "Drop Shadow")]
        public bool ShadowEnabled { get; set; }

        /// <summary>Gets or sets the most lines the name may wrap onto.</summary>
        [Display(Name = "Line Limit", Description = "The most lines a name may wrap onto.")]
        public int MaxLines { get; set; } = 2;

        /// <summary>Gets or sets the width of the space the lettering is laid out in.</summary>
        [Display(Name = "Width", Description = "The width of the space the lettering is laid out in.")]
        public int Width { get; set; } = 800;

        /// <summary>Gets or sets the height of the space the lettering is laid out in.</summary>
        [Display(Name = "Height", Description = "The room the lettering gets. The finished logo is trimmed to its artwork.")]
        public int Height { get; set; } = 310;

        /// <summary>Gets the font file to use, or null when the custom font is off.</summary>
        [JsonIgnore]
        public string? EffectiveFontPath => UseCustomFont ? FontPath : null;

        /// <summary>Creates a shallow copy for render-time adjustments.</summary>
        public LogoSettings Clone() => (LogoSettings)MemberwiseClone();
    }
}
