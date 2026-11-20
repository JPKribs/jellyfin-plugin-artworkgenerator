using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    /// <summary>
    /// How a text logo is generated from a series name.
    /// </summary>
    public class LogoSettings
    {
        /// <summary>Gets or sets where the logo text comes from.</summary>
        [Display(Name = TranslationKeys.LabelLogoSource, Description = TranslationKeys.TitleSourceHelp)]
        public LogoTitleSource TitleSource { get; set; } = LogoTitleSource.Title;

        /// <summary>Gets or sets a value indicating whether a year such as (2019) is removed.</summary>
        [Display(Name = TranslationKeys.LabelRemoveYear, Description = TranslationKeys.StripYearHelp)]
        public bool StripYear { get; set; } = true;

        /// <summary>Gets or sets how a name with a subtitle, such as "Star Wars: Andor", is laid out.</summary>
        [Display(Name = TranslationKeys.LabelNamesWithASubtitle, Description = TranslationKeys.SubtitleModeHelp)]
        public LogoSubtitleMode SubtitleMode { get; set; } = LogoSubtitleMode.Keep;

        /// <summary>
        /// Gets or sets the small line's size as a percent of the large line, when the subtitle mode
        /// draws the title and subtitle at two sizes.
        /// </summary>
        [Display(Name = TranslationKeys.LabelSmallLineSizePercent, Description = TranslationKeys.SecondarySizeHelp)]
        public float SecondarySize { get; set; } = 45.0f;

        /// <summary>Gets or sets an optional regular expression whose matches are removed from the text.</summary>
        [Display(Name = TranslationKeys.LabelRemovePattern, Description = TranslationKeys.CustomRegexHelp)]
        public string CustomRegex { get; set; } = string.Empty;

        /// <summary>Gets or sets how the letters are cased, whatever case the name arrives in.</summary>
        [Display(Name = TranslationKeys.LabelLetterCase, Description = TranslationKeys.LetterCaseHelp)]
        public LogoCase LetterCase { get; set; }

        [Display(Name = TranslationKeys.LabelFont)]
        public string FontFamily { get; set; } = "Montserrat";

        [Display(Name = TranslationKeys.LabelFontStyle)]
        public string FontStyle { get; set; } = "Bold";

        [Display(Name = TranslationKeys.LabelUseCustomFont, Description = TranslationKeys.UseCustomFontHelp)]
        public bool UseCustomFont { get; set; }

        [Display(Name = TranslationKeys.LabelFontPath, Description = TranslationKeys.SecondaryFontPathHelp)]
        public string FontPath { get; set; } = string.Empty;

        /// <summary>Gets or sets what fills the letters: a color, or a frame from the series.</summary>
        [Display(Name = TranslationKeys.LabelLettersFilledWith, Description = TranslationKeys.FillHelp)]
        public LogoFill Fill { get; set; } = LogoFill.Color;

        /// <summary>Gets or sets where the text color comes from, when the fill is a color.</summary>
        [Display(Name = TranslationKeys.LabelColorFrom, Description = TranslationKeys.ColorSourceHelp)]
        public LogoColorSource ColorSource { get; set; } = LogoColorSource.Fixed;

        /// <summary>Gets or sets the ARGB text color, and the fallback when sampling fails.</summary>
        [Display(Name = TranslationKeys.LabelLogoTextColor)]
        public string Color { get; set; } = "#FFFFFFFF";

        [Display(Name = TranslationKeys.LabelOutline)]
        public bool OutlineEnabled { get; set; }

        [Display(Name = TranslationKeys.LabelOutlineColor)]
        public string OutlineColor { get; set; } = "#FF000000";

        /// <summary>Gets or sets the outline width as a percent of the font size.</summary>
        [Display(Name = TranslationKeys.LabelOutlineWidthPercent, Description = TranslationKeys.OutlineWidthHelp)]
        public float OutlineWidth { get; set; } = 4.0f;

        [Display(Name = TranslationKeys.DropShadow)]
        public bool ShadowEnabled { get; set; }

        /// <summary>Gets or sets the most lines the name may wrap onto.</summary>
        [Display(Name = TranslationKeys.LabelLineLimit, Description = TranslationKeys.MaxLinesHelp)]
        public int MaxLines { get; set; } = 2;

        /// <summary>Gets or sets the width of the space the lettering is laid out in.</summary>
        [Display(Name = TranslationKeys.LabelWidth, Description = TranslationKeys.WidthHelp)]
        public int Width { get; set; } = 800;

        /// <summary>Gets or sets the height of the space the lettering is laid out in.</summary>
        [Display(Name = TranslationKeys.LabelHeight, Description = TranslationKeys.HeightHelp)]
        public int Height { get; set; } = 310;

        /// <summary>Gets the font file to use, or null when the custom font is off.</summary>
        [JsonIgnore]
        public string? EffectiveFontPath => UseCustomFont ? FontPath : null;

        /// <summary>Creates a shallow copy for render-time adjustments.</summary>
        public LogoSettings Clone() => (LogoSettings)MemberwiseClone();
    }
}
