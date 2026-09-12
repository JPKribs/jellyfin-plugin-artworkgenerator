using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Models
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

        public CanvasSource CanvasSource { get; set; } = CanvasSource.Extract;

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

        public PosterStyle PosterStyle { get; set; } = PosterStyle.Standard;

        /// <summary>
        /// Gets or sets the shape being rendered. Set at render time, never stored: every design
        /// renders both shapes, and the profile slot decides which one an image needs.
        /// </summary>
        [XmlIgnore]
        [JsonIgnore]
        public ArtworkShape Shape { get; set; } = ArtworkShape.Landscape;

        public CutoutType CutoutType { get; set; } = CutoutType.Code;

        /// <summary>
        /// Gets or sets how a framed poster fills its two edges. The "first" choices fill that edge
        /// with whichever line the item has, so a poster carrying one line always looks the same;
        /// the "always" choices pin the title to an edge and leave it empty when there is no title.
        /// </summary>
        public TextEdge TextEdge { get; set; } = TextEdge.TopFirst;

        public bool CutoutBorder { get; set; } = true;

        public Position LogoPosition { get; set; } = Position.Center;

        public Alignment LogoAlignment { get; set; } = Alignment.Center;

        public float LogoHeight { get; set; } = 30.0f;

        public float BrightenHDR { get; set; } = 25.0f;

        public PosterFill PosterFill { get; set; } = PosterFill.Original;

        public string PosterDimensionRatio { get; set; } = "16:9";

        /// <summary>
        /// Gets or sets the aspect ratio for portrait renders, such as series and season posters.
        /// </summary>
        public string PortraitDimensionRatio { get; set; } = "2:3";

        public float PosterSafeArea { get; set; } = 5.0f;

        /// <summary>
        /// Vertical gap between stacked poster elements — logo, episode code, title, and the
        /// blocks each style reserves for them — as a percentage of the poster's short side. This is
        /// the single knob every style uses to keep elements apart, so raising it pushes them
        /// further from each other everywhere rather than in one style.
        /// </summary>
        public float ElementSpacing { get; set; } = 2.0f;

        public bool ShowSecondary { get; set; } = true;

        public string SecondaryFontFamily { get; set; } = "Arial";

        public bool SecondaryUseCustomFont { get; set; } = false;

        public string SecondaryFontPath { get; set; } = string.Empty;

        public string SecondaryFontStyle { get; set; } = "Bold";

        public float SecondaryFontSize { get; set; } = 7.0F;

        public string SecondaryFontColor { get; set; } = "#FFFFFFFF";

        public bool ShowPrimary { get; set; } = true;

        public string PrimaryFontFamily { get; set; } = "Arial";

        public bool PrimaryUseCustomFont { get; set; } = false;

        public string PrimaryFontPath { get; set; } = string.Empty;

        public string PrimaryFontStyle { get; set; } = "Bold";

        public float PrimaryFontSize { get; set; } = 10.0F;

        public string PrimaryFontColor { get; set; } = "#FFFFFFFF";

        /// <summary>
        /// How to handle episode titles that do not fit the poster's text area.
        /// </summary>
        public LongTextHandling LongTextHandling { get; set; } = LongTextHandling.Ellipsis;

        public string OverlayColor { get; set; } = "#66000000";

        public OverlayGradient OverlayGradient { get; set; } = OverlayGradient.None;

        public string OverlaySecondaryColor { get; set; } = "#66000000";

        /// <summary>
        /// When enabled, the overlay color channels are replaced per episode by the dominant
        /// color sampled from the canvas image. The configured alpha values are preserved.
        /// </summary>
        public bool PaletteDerivedColors { get; set; }

        public string GraphicPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the graphic's size as a percent of the poster's short side. The graphic is
        /// fitted inside a box that size, so it always keeps its own proportions.
        /// </summary>
        public float GraphicSize { get; set; } = 25.0f;

        /// <summary>
        /// Legacy per-axis width, retained only to migrate designs that sized the graphic on each
        /// axis independently, which could stretch it. Null on new designs.
        /// </summary>
        public float? GraphicWidth { get; set; }

        /// <summary>Legacy per-axis height. See <see cref="GraphicWidth"/>.</summary>
        public float? GraphicHeight { get; set; }

        public Position GraphicPosition { get; set; } = Position.Center;

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
