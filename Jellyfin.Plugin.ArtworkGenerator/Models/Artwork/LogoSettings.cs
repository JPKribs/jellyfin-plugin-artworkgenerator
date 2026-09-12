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
        [Display(Name = "Name From", Description = "Which name the logo shows. Falls back to the title when the chosen name is empty.")]
        public LogoTitleSource TitleSource { get; set; } = LogoTitleSource.Title;

        /// <summary>Gets or sets a value indicating whether a year such as (2019) is removed.</summary>
        [Display(Name = "Remove Year", Description = "Remove a year in brackets, such as (2019). Folder names also lose a trailing year and tags like [tvdbid-12345].")]
        public bool StripYear { get; set; } = true;

        /// <summary>Gets or sets how a name with a subtitle, such as "Star Wars: Andor", is laid out.</summary>
        [Display(Name = "Names With a Subtitle", Description = "How to treat a name split by a colon or spaced dash, such as \"Star Wars: Andor\". The last two draw both parts at two sizes, the way a spin-off's own logo usually looks. A name with no colon or dash is always drawn whole.")]
        public LogoSubtitleMode SubtitleMode { get; set; } = LogoSubtitleMode.Keep;

        /// <summary>
        /// Gets or sets the small line's size as a percent of the large line, when the subtitle mode
        /// draws the title and subtitle at two sizes.
        /// </summary>
        [Display(Name = "Small Line Size (%)", Description = "The small line's size as a percent of the large one. Default is 45.")]
        public float SecondarySize { get; set; } = 45.0f;

        /// <summary>Gets or sets an optional regular expression whose matches are removed from the text.</summary>
        [Display(Name = "Remove Pattern", Description = "Optional regular expression. Anything it matches is removed. An invalid pattern is ignored.")]
        public string CustomRegex { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether the text is drawn in capitals.</summary>
        [Display(Name = "All Capitals")]
        public bool Uppercase { get; set; }

        [Display(Name = "Font")]
        public string FontFamily { get; set; } = "Arial";

        [Display(Name = "Font Style")]
        public string FontStyle { get; set; } = "Bold";

        [Display(Name = "Use Custom Font", Description = "Use a font file instead of an installed font.")]
        public bool UseCustomFont { get; set; }

        [Display(Name = "Font Path", Description = "Path to a TTF, OTF, or TTC font file readable by the server.")]
        public string FontPath { get; set; } = string.Empty;

        /// <summary>Gets or sets what fills the letters: a colour, or a frame from the series.</summary>
        [Display(Name = "Letters Filled With", Description = "A frame fill cuts the letters out of a picture from the show, using the same frames its other images come from. A heavy font shows more of the picture, and an outline keeps the letters readable over a busy background.")]
        public LogoFill Fill { get; set; } = LogoFill.Color;

        /// <summary>Gets or sets where the text colour comes from, when the fill is a colour.</summary>
        [Display(Name = "Color From", Description = "Sample the main color of the series artwork, lifted to stay legible, or use a color you pick.")]
        public LogoColorSource ColorSource { get; set; } = LogoColorSource.Fixed;

        /// <summary>Gets or sets the ARGB text colour, and the fallback when sampling fails.</summary>
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

        /// <summary>Gets or sets the most lines the name may wrap onto, 1 or 2.</summary>
        [Display(Name = "Lines", Description = "A long name splits onto two lines only when that lets the text grow noticeably.")]
        public int MaxLines { get; set; } = 2;

        /// <summary>Gets or sets the canvas width. 800 by 310 matches the common HD clear logo size.</summary>
        [Display(Name = "Width")]
        public int Width { get; set; } = 800;

        /// <summary>Gets or sets the canvas height.</summary>
        [Display(Name = "Height", Description = "Canvas size in pixels. 800 by 310 matches the common HD clear logo size, so generated logos sit alongside downloaded ones.")]
        public int Height { get; set; } = 310;

        /// <summary>Gets the font file to use, or null when the custom font is off.</summary>
        [JsonIgnore]
        public string? EffectiveFontPath => UseCustomFont ? FontPath : null;

        /// <summary>Creates a shallow copy for render-time adjustments.</summary>
        public LogoSettings Clone() => (LogoSettings)MemberwiseClone();
    }
}
