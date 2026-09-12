using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    public partial class PosterSettings
    {
        /// <summary>
        /// Gets the effective episode font path (null when custom font is disabled).
        /// </summary>
        [JsonIgnore]
        public string? EffectiveSecondaryFontPath => SecondaryUseCustomFont ? SecondaryFontPath : null;

        /// <summary>
        /// Gets the effective title font path (null when custom font is disabled).
        /// </summary>
        [JsonIgnore]
        public string? EffectivePrimaryFontPath => PrimaryUseCustomFont ? PrimaryFontPath : null;

        /// <summary>
        /// Legacy flag retained only for migration of pre-10.11.23 configurations.
        /// Null on new configurations; when present it is migrated to <see cref="CanvasSource"/>.
        /// </summary>
        public bool? ExtractPoster { get; set; }

        [Display(Name = "Canvas Background", Description = "Determine where the poster background should come from.")]
        public CanvasSource CanvasSource { get; set; } = CanvasSource.Extract;

        /// <summary>
        /// When the canvas is an extracted frame, also upload the cropped canvas
        /// as the episode's backdrop image (the rendered poster remains the primary image).
        /// </summary>
        public bool GenerateBackdrop { get; set; }

        [Display(Name = "Enable Letterbox Detection", Description = "Detect and crop black bars from extracted frames.")]
        public bool EnableLetterboxDetection { get; set; } = true;

        public int LetterboxBlackThreshold { get; set; } = 25;

        public float LetterboxConfidence { get; set; } = 85.0f;

        [Display(Name = "Extraction Start (%)")]
        public float ExtractWindowStart { get; set; } = 20.0f;

        [Display(Name = "Extraction End (%)", Description = "Stop extracting at this percent of each episode.")]
        public float ExtractWindowEnd { get; set; } = 80.0f;

        [Display(Name = "Style")]
        public PosterStyle PosterStyle { get; set; } = PosterStyle.Standard;

        /// <summary>
        /// Gets or sets the shape being rendered. Set at render time, never stored: every design
        /// renders both shapes, and the profile slot decides which one an image needs.
        /// </summary>
        [XmlIgnore]
        [JsonIgnore]
        public ArtworkShape Shape { get; set; } = ArtworkShape.Landscape;

        [Display(Name = "Type", Description = "Show the episode as a code like S01E01 or as a word like ONE.")]
        public CutoutType CutoutType { get; set; } = CutoutType.Code;


        /// <summary>
        /// Gets or sets where the title and subtitle sit inside the safe area. Left on its default,
        /// each design keeps the placement it was drawn around.
        /// </summary>
        [Display(Name = "Text Position", Description = "Where the title and subtitle sit.")]
        public TextPosition TextPosition { get; set; } = TextPosition.Auto;

        /// <summary>
        /// Gets or sets which side the title and subtitle are pulled to. Left on its default, each
        /// design keeps the side it was drawn around.
        /// </summary>
        [Display(Name = "Text Alignment", Description = "Which side the title and subtitle are pulled to.")]
        public TextAlignment TextAlignment { get; set; } = TextAlignment.Auto;

        /// <summary>
        /// Gets or sets a value indicating whether an item carrying only one of the two lines puts
        /// it where the title goes. Off, the title's place is left empty and the lone subtitle stays
        /// in its own.
        /// </summary>
        [Display(Name = "Lone Subtitle Uses the Title's Edge", Description = "An item with no title of its own puts its subtitle where the title would go.")]
        public bool LoneLineFollowsTitle { get; set; } = true;

        [Display(Name = "Enable Outline", Description = "Draw a contrasting outline around the cut-out shape: the text for Cutout, the brush stroke for Brush.")]
        public bool CutoutBorder { get; set; } = true;

        [Display(Name = "Logo Position", Description = "Vertical position of the series logo on the poster.")]
        public Position LogoPosition { get; set; } = Position.Center;

        [Display(Name = "Logo Alignment", Description = "Horizontal alignment of the series logo on the poster.")]
        public Alignment LogoAlignment { get; set; } = Alignment.Center;

        [Display(Name = "Logo Height", Description = "Logo height as a percent of the poster's short side, 1 to 100.")]
        public float LogoHeight { get; set; } = 30.0f;

        [Display(Name = "Brighten Frame (%)", Description = "Brightens every extracted frame by this percent.")]
        public float BrightenHDR { get; set; }

        [Display(Name = "Fill Strategy", Description = "How the image is resized to fit the poster.")]
        public PosterFill PosterFill { get; set; } = PosterFill.Original;

        [Display(Name = "Landscape Aspect Ratio")]
        public string PosterDimensionRatio { get; set; } = "16:9";

        /// <summary>
        /// Gets or sets the aspect ratio for portrait renders, such as series and season posters.
        /// </summary>
        [Display(Name = "Portrait Aspect Ratio")]
        public string PortraitDimensionRatio { get; set; } = "2:3";

        [Display(Name = "Safe Area", Description = "Margin kept clear around all edges, as a percent of the poster's short side.")]
        public float PosterSafeArea { get; set; } = 5.0f;

        /// <summary>
        /// Vertical gap between stacked poster elements — logo, episode code, title, and the
        /// blocks each style reserves for them — as a percentage of the poster's short side. This is
        /// the single knob every style uses to keep elements apart, so raising it pushes them
        /// further from each other everywhere rather than in one style.
        /// </summary>
        [Display(Name = "Element Spacing", Description = "Gap between stacked elements, as a percent of the short side.")]
        public float ElementSpacing { get; set; } = 2.0f;

        [Display(Name = "Show Subtitle", Description = "Draw the subtitle, the smaller line.")]
        public bool ShowSecondary { get; set; } = true;

        [Display(Name = "Font", Description = "Font family for the subtitle.")]
        public string SecondaryFontFamily { get; set; } = "Arial";

        [Display(Name = "Use Custom Font", Description = "Use a font file instead of a built in font.")]
        public bool SecondaryUseCustomFont { get; set; } = false;

        [Display(Name = "Font Path", Description = "Path to a TTF, OTF, or TTC font file readable by the server.")]
        public string SecondaryFontPath { get; set; } = string.Empty;

        [Display(Name = "Font Style", Description = "Font style for the subtitle.")]
        public string SecondaryFontStyle { get; set; } = "Bold";

        [Display(Name = "Font Size", Description = "Text size as a percent of the poster's short side, 1 to 100.")]
        public float SecondaryFontSize { get; set; } = 7.0F;

        [Display(Name = "Font Color")]
        public string SecondaryFontColor { get; set; } = "#FFFFFFFF";

        [Display(Name = "Show Title", Description = "Draw the title, the larger line.")]
        public bool ShowPrimary { get; set; } = true;

        [Display(Name = "Font", Description = "Font family for the title text.")]
        public string PrimaryFontFamily { get; set; } = "Arial";

        [Display(Name = "Use Custom Font", Description = "Use a font file instead of a built in font.")]
        public bool PrimaryUseCustomFont { get; set; } = false;

        [Display(Name = "Font Path", Description = "Path to a TTF, OTF, or TTC font file readable by the server.")]
        public string PrimaryFontPath { get; set; } = string.Empty;

        [Display(Name = "Font Style", Description = "Font style for the title text.")]
        public string PrimaryFontStyle { get; set; } = "Bold";

        [Display(Name = "Font Size", Description = "Title size as a percent of the poster's short side, 1 to 100.")]
        public float PrimaryFontSize { get; set; } = 10.0F;

        [Display(Name = "Font Color")]
        public string PrimaryFontColor { get; set; } = "#FFFFFFFF";

        /// <summary>
        /// How to handle episode titles that do not fit the poster's text area.
        /// </summary>
        [Display(Name = "Long Titles", Description = "What to do when a title does not fit.")]
        public LongTextHandling LongTextHandling { get; set; } = LongTextHandling.Ellipsis;

        /// <summary>
        /// Gets or sets what a subtitle does when it does not fit the width it has.
        /// </summary>
        [Display(Name = "Long Subtitles", Description = "What to do when the subtitle does not fit.")]
        public LongSubtitleHandling LongSubtitleHandling { get; set; }

        [Display(Name = "Overlay Color")]
        public string OverlayColor { get; set; } = "#66000000";

        [Display(Name = "Overlay Gradient", Description = "Gradient direction from the overlay color to the secondary color.")]
        public OverlayGradient OverlayGradient { get; set; } = OverlayGradient.None;

        [Display(Name = "Secondary Overlay Color")]
        public string OverlaySecondaryColor { get; set; } = "#66000000";

        /// <summary>
        /// When enabled, the overlay color channels are replaced per episode by the dominant
        /// color sampled from the canvas image. The configured alpha values are preserved.
        /// </summary>
        [Display(Name = "Palette-Derived Colors", Description = "Take the overlay colors from each image.")]
        public bool PaletteDerivedColors { get; set; }

        [Display(Name = "Graphic File Path", Description = "Optional graphic drawn above the image and below the text.")]
        public string GraphicPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the graphic's size as a percent of the poster's short side. The graphic is
        /// fitted inside a box that size, so it always keeps its own proportions.
        /// </summary>
        [Display(Name = "Graphic Size (%)", Description = "Size of the graphic as a percent of the short side.")]
        public float GraphicSize { get; set; } = 25.0f;

        /// <summary>
        /// Legacy per-axis width, retained only to migrate designs that sized the graphic on each
        /// axis independently, which could stretch it. Null on new designs.
        /// </summary>
        public float? GraphicWidth { get; set; }

        /// <summary>Legacy per-axis height. See <see cref="GraphicWidth"/>.</summary>
        public float? GraphicHeight { get; set; }

        [Display(Name = "Graphic Position", Description = "Vertical position of the static graphic on the poster.")]
        public Position GraphicPosition { get; set; } = Position.Center;

        [Display(Name = "Graphic Alignment", Description = "Horizontal alignment of the static graphic on the poster.")]
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
