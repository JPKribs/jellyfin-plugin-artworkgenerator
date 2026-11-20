using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Xml.Serialization;
using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    public class PosterSettings
    {
        /// <summary>
        /// Gets the effective subtitle font path (null when custom font is disabled).
        /// </summary>
        [JsonIgnore]
        public string? EffectiveSecondaryFontPath => SecondaryUseCustomFont ? SecondaryFontPath : null;

        /// <summary>
        /// Gets the effective title font path (null when custom font is disabled).
        /// </summary>
        [JsonIgnore]
        public string? EffectivePrimaryFontPath => PrimaryUseCustomFont ? PrimaryFontPath : null;

        [Display(Name = TranslationKeys.LabelCanvasBackground, Description = TranslationKeys.CanvasSourceHelp)]
        public CanvasSource CanvasSource { get; set; } = CanvasSource.Extract;

        /// <summary>Gets or sets how many frames a grid canvas holds.</summary>
        [Display(Name = TranslationKeys.LabelGridFrames, Description = TranslationKeys.GridFramesHelp)]
        public int GridFrames { get; set; } = 4;

        /// <summary>Gets or sets the gap between grid cells, as a percent of the short side.</summary>
        [Display(Name = TranslationKeys.LabelGridGapPercent, Description = TranslationKeys.GridGapHelp)]
        public float GridGap { get; set; } = 2.0f;

        /// <summary>
        /// When the canvas is an extracted frame, also upload the cropped canvas
        /// as the episode's backdrop image (the rendered poster remains the primary image).
        /// </summary>
        public bool GenerateBackdrop { get; set; }

        public bool EnableLetterboxDetection { get; set; } = true;

        public int LetterboxBlackThreshold { get; set; } = 25;

        public float LetterboxConfidence { get; set; } = 85.0f;

        public float ExtractWindowStart { get; set; } = 20.0f;

        public float ExtractWindowEnd { get; set; } = 80.0f;

        [Display(Name = TranslationKeys.LabelStyle)]
        public PosterStyle PosterStyle { get; set; } = PosterStyle.Standard;

        /// <summary>
        /// Gets or sets the shape being rendered. Set at render time, never stored: every design
        /// renders both shapes, and the profile slot decides which one an image needs.
        /// </summary>
        [XmlIgnore]
        [JsonIgnore]
        public ArtworkShape Shape { get; set; } = ArtworkShape.Landscape;

        [Display(Name = TranslationKeys.LabelType, Description = TranslationKeys.CutoutTypeHelp)]
        public CutoutType CutoutType { get; set; } = CutoutType.Code;

        /// <summary>
        /// Gets or sets where the title and subtitle sit inside the safe area. Left on its default,
        /// each design keeps the placement it was drawn around.
        /// </summary>
        [Display(Name = TranslationKeys.LabelTextPosition, Description = TranslationKeys.TextPositionHelp)]
        public TextPosition TextPosition { get; set; } = TextPosition.Auto;

        /// <summary>
        /// Gets or sets which side the title and subtitle are pulled to. Left on its default, each
        /// design keeps the side it was drawn around.
        /// </summary>
        [Display(Name = TranslationKeys.LabelTextAlignment, Description = TranslationKeys.TextAlignmentHelp)]
        public TextAlignment TextAlignment { get; set; } = TextAlignment.Auto;

        /// <summary>
        /// Gets or sets a value indicating whether an item carrying only one of the two lines puts
        /// it where the title goes. Off, the title's place is left empty and the lone subtitle stays
        /// in its own.
        /// </summary>
        [Display(Name = TranslationKeys.LabelLoneSubtitleTakesTheTitleEdge, Description = TranslationKeys.LoneLineFollowsTitleHelp)]
        public bool LoneLineFollowsTitle { get; set; } = true;

        [Display(Name = TranslationKeys.LabelOutline, Description = TranslationKeys.CutoutBorderHelp)]
        public bool CutoutBorder { get; set; } = true;

        [Display(Name = TranslationKeys.LabelLogoPosition, Description = TranslationKeys.LogoPositionHelp)]
        public Position LogoPosition { get; set; } = Position.Center;

        [Display(Name = TranslationKeys.LabelLogoAlignment, Description = TranslationKeys.LogoAlignmentHelp)]
        public Alignment LogoAlignment { get; set; } = Alignment.Center;

        [Display(Name = TranslationKeys.LabelLogoHeight, Description = TranslationKeys.LogoHeightHelp)]
        public float LogoHeight { get; set; } = 30.0f;

        public float BrightenHDR { get; set; }

        [Display(Name = TranslationKeys.LabelFillStrategy, Description = TranslationKeys.PosterFillHelp)]
        public PosterFill PosterFill { get; set; } = PosterFill.Original;

        [Display(Name = TranslationKeys.LabelLandscapeAspectRatio)]
        public string PosterDimensionRatio { get; set; } = "16:9";

        /// <summary>
        /// Gets or sets the aspect ratio for portrait renders, such as series and season posters.
        /// </summary>
        [Display(Name = TranslationKeys.LabelPortraitAspectRatio)]
        public string PortraitDimensionRatio { get; set; } = "2:3";

        [Display(Name = TranslationKeys.LabelSafeArea, Description = TranslationKeys.PosterSafeAreaHelp)]
        public float PosterSafeArea { get; set; } = 5.0f;

        /// <summary>
        /// Vertical gap between stacked poster elements — logo, episode code, title, and the
        /// blocks each style reserves for them — as a percentage of the poster's short side. This is
        /// the single knob every style uses to keep elements apart, so raising it pushes them
        /// further from each other everywhere rather than in one style.
        /// </summary>
        [Display(Name = TranslationKeys.LabelElementSpacing, Description = TranslationKeys.ElementSpacingHelp)]
        public float ElementSpacing { get; set; } = 2.0f;

        [Display(Name = TranslationKeys.LabelShowSubtitle, Description = TranslationKeys.ShowSecondaryHelp)]
        public bool ShowSecondary { get; set; } = true;

        [Display(Name = TranslationKeys.LabelFont, Description = TranslationKeys.SecondaryFontFamilyHelp)]
        public string SecondaryFontFamily { get; set; } = "Montserrat";

        [Display(Name = TranslationKeys.LabelUseCustomFont, Description = TranslationKeys.SecondaryUseCustomFontHelp)]
        public bool SecondaryUseCustomFont { get; set; } = false;

        [Display(Name = TranslationKeys.LabelFontPath, Description = TranslationKeys.SecondaryFontPathHelp)]
        public string SecondaryFontPath { get; set; } = string.Empty;

        [Display(Name = TranslationKeys.LabelFontStyle, Description = TranslationKeys.SecondaryFontStyleHelp)]
        public string SecondaryFontStyle { get; set; } = "Bold";

        [Display(Name = TranslationKeys.LabelFontSize, Description = TranslationKeys.SecondaryFontSizeHelp)]
        public float SecondaryFontSize { get; set; } = 7.0F;

        [Display(Name = TranslationKeys.LabelFontColor)]
        public string SecondaryFontColor { get; set; } = "#FFFFFFFF";

        [Display(Name = TranslationKeys.LabelShowTitle, Description = TranslationKeys.ShowPrimaryHelp)]
        public bool ShowPrimary { get; set; } = true;

        [Display(Name = TranslationKeys.LabelFont, Description = TranslationKeys.PrimaryFontFamilyHelp)]
        public string PrimaryFontFamily { get; set; } = "Montserrat";

        [Display(Name = TranslationKeys.LabelUseCustomFont, Description = TranslationKeys.SecondaryUseCustomFontHelp)]
        public bool PrimaryUseCustomFont { get; set; } = false;

        [Display(Name = TranslationKeys.LabelFontPath, Description = TranslationKeys.SecondaryFontPathHelp)]
        public string PrimaryFontPath { get; set; } = string.Empty;

        [Display(Name = TranslationKeys.LabelFontStyle, Description = TranslationKeys.PrimaryFontStyleHelp)]
        public string PrimaryFontStyle { get; set; } = "Bold";

        [Display(Name = TranslationKeys.LabelFontSize, Description = TranslationKeys.PrimaryFontSizeHelp)]
        public float PrimaryFontSize { get; set; } = 10.0F;

        [Display(Name = TranslationKeys.LabelFontColor)]
        public string PrimaryFontColor { get; set; } = "#FFFFFFFF";

        /// <summary>
        /// How to handle episode titles that do not fit the poster's text area.
        /// </summary>
        [Display(Name = TranslationKeys.LabelLongTitles, Description = TranslationKeys.LongTextHandlingHelp)]
        public LongTextHandling LongTextHandling { get; set; } = LongTextHandling.Ellipsis;

        /// <summary>
        /// Gets or sets what a subtitle does when it does not fit the width it has.
        /// </summary>
        [Display(Name = TranslationKeys.LabelLongSubtitles, Description = TranslationKeys.LongSubtitleHandlingHelp)]
        public LongSubtitleHandling LongSubtitleHandling { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the subtitle is dropped when it says the same
        /// thing as the title, such as a season named "Season 12" whose label is also "Season 12".
        /// </summary>
        [Display(Name = TranslationKeys.LabelHideARepeatedSubtitle, Description = TranslationKeys.HideRepeatedSubtitleHelp)]
        public bool HideRepeatedSubtitle { get; set; }

        [Display(Name = TranslationKeys.LabelOverlayColor)]
        public string OverlayColor { get; set; } = "#66000000";

        [Display(Name = TranslationKeys.LabelOverlayGradient, Description = TranslationKeys.OverlayGradientHelp)]
        public OverlayGradient OverlayGradient { get; set; } = OverlayGradient.None;

        [Display(Name = TranslationKeys.LabelSecondaryOverlayColor)]
        public string OverlaySecondaryColor { get; set; } = "#66000000";

        /// <summary>
        /// When enabled, the overlay color channels are replaced per episode by the dominant
        /// color sampled from the canvas image. The configured alpha values are preserved.
        /// </summary>
        [Display(Name = TranslationKeys.LabelPaletteColors, Description = TranslationKeys.PaletteDerivedColorsHelp)]
        public bool PaletteDerivedColors { get; set; }

        [Display(Name = TranslationKeys.LabelGraphicFilePath, Description = TranslationKeys.GraphicPathHelp)]
        public string GraphicPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the graphic's size as a percent of the poster's short side. The graphic is
        /// fitted inside a box that size, so it always keeps its own proportions.
        /// </summary>
        [Display(Name = TranslationKeys.LabelGraphicSizePercent, Description = TranslationKeys.GraphicSizeHelp)]
        public float GraphicSize { get; set; } = 25.0f;

        [Display(Name = TranslationKeys.LabelGraphicPosition, Description = TranslationKeys.GraphicPositionHelp)]
        public Position GraphicPosition { get; set; } = Position.Center;

        [Display(Name = TranslationKeys.LabelGraphicAlignment, Description = TranslationKeys.GraphicAlignmentHelp)]
        public Alignment GraphicAlignment { get; set; } = Alignment.Center;

        /// <summary>
        /// Creates a shallow copy for per-episode render-time adjustments (e.g. palette-derived
        /// colors) without mutating the shared configuration instance.
        /// </summary>
        public PosterSettings Clone()
        {
            return (PosterSettings)MemberwiseClone();
        }
    }
}
