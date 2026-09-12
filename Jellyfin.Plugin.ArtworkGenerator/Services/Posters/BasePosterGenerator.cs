using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SkiaSharp;
using Jellyfin.Plugin.ArtworkGenerator.Configuration;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Posters
{
    public interface IPosterGenerator
    {
        // Generate
        // Generates a poster from a provided canvas and episode metadata using layered rendering,
        // returning the encoded JPEG bytes, or null when rendering failed.
        byte[]? Generate(
            SKBitmap canvas,
            ArtworkSubject subject,
            PosterSettings settings);

        // Style
        // The poster style this generator produces.
        PosterStyle Style { get; }

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        string Description { get; }

        // PrimaryDescription
        // One sentence saying what the title is and where this style puts it, shown at the top of
        // the Title section so the settings below it read in context.
        string PrimaryDescription { get; }

        // SecondaryDescription
        // One sentence saying what the subtitle is and where this style puts it, shown at the top
        // of the Subtitle section.
        string SecondaryDescription { get; }

        // SupportedShapes
        // The poster shapes this style can lay out.
        ArtworkShapes SupportedShapes { get; }

        // Supports
        // Returns true when this style can lay out the given shape.
        bool Supports(ArtworkShape shape);

        // SettingRules
        // Which settings this style uses, and which it always draws, so the configuration page can
        // offer exactly the settings that do something.
        IReadOnlyDictionary<string, PosterSettingState> SettingRules { get; }

        // SettingText
        // A style's own wording for a shared setting. Text position means "where the text sits" on
        // most designs and "which border edge the title takes" on a framed one; rather than word it
        // vaguely enough to cover both, a style that reads differently says so here.
        IReadOnlyDictionary<string, SettingText> SettingText { get; }
    }

    public abstract partial class BasePosterGenerator : IPosterGenerator
    {
        // Logger
        // The style's own logger, for the few styles that report more than a failed render.
        protected ILogger Logger { get; }

        // BasePosterGenerator
        // Every style logs the same way about the same pipeline, so the logger lives here. Styles
        // still pass their own typed logger, which keeps each one's log category its own.
        protected BasePosterGenerator(ILogger logger)
        {
            Logger = logger;
        }

        // Style
        // The poster style this generator produces.
        public abstract PosterStyle Style { get; }

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        public abstract string Description { get; }

        // PrimaryDescription
        // Most styles set the title in a text block at the foot of the image, so that is the
        // default; a style that treats it differently says so.
        public virtual string PrimaryDescription
            => "The title is the item's own name, set at the bottom of the image under the subtitle.";

        // SecondaryDescription
        // What fills the subtitle never changes, only where a style puts it, so the default names
        // the content and the usual place.
        public virtual string SecondaryDescription
            => "The subtitle is an episode's numbers, a season's label, or a film's year, set above the title.";

        // SupportedShapes
        // Every style lays out both shapes unless it says otherwise.
        public virtual ArtworkShapes SupportedShapes => ArtworkShapes.All;

        // SettingRules
        // A style offers the shared settings and none of the style-specific ones unless it says
        // otherwise. Overriding this is how a style adds its own settings, or insists on an element
        // it always draws.
        public virtual IReadOnlyDictionary<string, PosterSettingState> SettingRules => PosterSettingRules.None;

        // SettingText
        // A style uses the settings' own wording unless it says otherwise.
        public virtual IReadOnlyDictionary<string, SettingText> SettingText { get; }
            = new Dictionary<string, SettingText>(StringComparer.Ordinal);

        // Supports
        // Returns true when this style can lay out the given shape.
        public bool Supports(ArtworkShape shape)
        {
            var flag = shape == ArtworkShape.Portrait ? ArtworkShapes.Portrait : ArtworkShapes.Landscape;
            return (SupportedShapes & flag) != 0;
        }

        // A title that is one word and the item's own number, such as "Staffel 12" or "Series 12".
        // Letters must come first, so "12 Monkeys" is a name rather than a numbering.
        private static readonly Regex NumberedName =
            new(@"^\p{L}+\s*0*(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // WithFrameExtraction
        // Stamps the server's frame extraction settings onto a design's render settings. The design
        // no longer chooses these, so whatever a saved design or an imported template still carries
        // is replaced by the one set that applies everywhere.
        public static PosterSettings WithFrameExtraction(PosterSettings settings, FrameExtractionSettings? extraction)
        {
            ArgumentNullException.ThrowIfNull(settings);

            if (extraction == null)
            {
                return settings;
            }

            settings.ExtractWindowStart = extraction.ExtractWindowStart;
            settings.ExtractWindowEnd = extraction.ExtractWindowEnd;
            settings.BrightenHDR = extraction.BrightenFrame;
            settings.EnableLetterboxDetection = extraction.EnableLetterboxDetection;
            settings.LetterboxBlackThreshold = extraction.LetterboxBlackThreshold;
            settings.LetterboxConfidence = extraction.LetterboxConfidence;

            return settings;
        }

        // SizeUnit
        // The length every size setting is a percentage of: the poster's short side. A landscape
        // poster's short side is what these sizes always measured against, so existing designs are
        // unchanged; a portrait poster has a shorter one, so a design's text keeps the same weight
        // relative to the frame instead of overflowing it.
        protected static int SizeUnit(int width, int height) => Math.Max(1, Math.Min(width, height));

        // GetSafeAreaMargin
        // Returns the safe area margin as a percentage of the poster dimensions.
        protected static float GetSafeAreaMargin(PosterSettings settings) => settings.PosterSafeArea / 100f;

        // GetElementSpacing
        // The configured gap between stacked elements, in pixels for this poster's short side.
        // Every style resolves spacing through here so one setting moves them all consistently.
        protected static float GetElementSpacing(PosterSettings settings, float unit)
            => unit * (Math.Max(0f, settings.ElementSpacing) / 100f);

        // Layout block keys shared by the styles that stack text against the bottom edge.
        // How far a line may be shrunk to fit before it is trimmed instead. Below this it stops
        // being readable, and a smaller unreadable line is worse than a shorter readable one.
        private const float MinimumFittedScale = 0.6f;

        protected const string SecondaryBlock = "secondary";
        protected const string SeparatorBlock = "separator";
        protected const string PrimaryBlock = "primary";

        // ApplySafeAreaConstraints
        // Calculates the safe area dimensions and offsets for a given poster size.
        // The margin is the safe area percent of the poster's short side, applied as the same
        // pixel amount on all four sides, so the border is visually even (10% of a
        // 1600x1000 poster is a 100 pixel margin both vertically and horizontally).
        protected static void ApplySafeAreaConstraints(
            int width, int height, PosterSettings settings,
            out float safeWidth, out float safeHeight, out float safeLeft, out float safeTop)
        {
            var marginPixels = SizeUnit(width, height) * GetSafeAreaMargin(settings);
            safeLeft = marginPixels;
            safeTop = marginPixels;
            safeWidth = width - (2 * marginPixels);
            safeHeight = height - (2 * marginPixels);
        }

        // Generate
        // Generates a poster using the 4-layer rendering pipeline and returns the encoded JPEG.
        public byte[]? Generate(SKBitmap canvas, ArtworkSubject subject, PosterSettings settings)
        {
            try
            {
                int width = canvas.Width;
                int height = canvas.Height;

                if (settings.PaletteDerivedColors)
                {
                    settings = ApplyDerivedPalette(settings, canvas);
                }

                var imageInfo = new SKImageInfo(
                    width,
                    height,
                    SKColorType.Rgba8888,
                    SKAlphaType.Premul,
                    SKColorSpace.CreateSrgb());

                using var surface = SKSurface.Create(imageInfo);
                var skCanvas = surface.Canvas;
                skCanvas.Clear(SKColors.Transparent);

                // Layer 1: Canvas (base layer)
                RenderCanvas(skCanvas, canvas, subject, settings, width, height);

                // Layer 2: Overlay (color tinting)
                RenderOverlay(skCanvas, subject, settings, width, height);

                // Layer 3: Graphics (static images/watermarks)
                RenderGraphics(skCanvas, subject, settings, width, height);

                // Layer 4: Typography (text and logos)
                RenderTypography(skCanvas, subject, settings, width, height);

                using var finalImage = surface.Snapshot();
                using var data = finalImage.Encode(SKEncodedImageFormat.Jpeg, RenderConstants.JpegQuality);

                return data?.ToArray();
            }
            catch (Exception ex)
            {
                LogError(ex, subject.EpisodeName);
                return null;
            }
        }

        // ApplyDerivedPalette
        // Returns a render-time copy of the settings whose overlay colors are replaced by the
        // dominant color sampled from the canvas (secondary gets a darkened variant for depth).
        // The configured alpha channels are preserved; a transparent canvas leaves settings unchanged.
        private static PosterSettings ApplyDerivedPalette(PosterSettings settings, SKBitmap canvas)
        {
            // An empty or zero-alpha overlay means "no overlay" — leave it alone. ParseHexColor
            // falls back to opaque white for empty/invalid strings, so deriving from it would
            // turn a deliberately disabled overlay into a fully opaque one.
            if (string.IsNullOrEmpty(settings.OverlayColor))
                return settings;

            var primaryAlpha = ColorUtils.ParseHexColor(settings.OverlayColor).Alpha;
            if (primaryAlpha == 0)
                return settings;

            var dominant = ColorUtils.GetDominantColor(canvas);
            if (dominant == SKColor.Empty)
                return settings;

            var derived = settings.Clone();
            derived.OverlayColor = ColorUtils.ToArgbHex(dominant.WithAlpha(primaryAlpha));

            // The secondary color only participates when it is itself enabled; a zero-alpha
            // secondary keeps its meaning ("fall back to primary") in RenderOverlay.
            var secondaryAlpha = string.IsNullOrEmpty(settings.OverlaySecondaryColor)
                ? (byte)0
                : ColorUtils.ParseHexColor(settings.OverlaySecondaryColor).Alpha;
            if (secondaryAlpha > 0)
            {
                derived.OverlaySecondaryColor = ColorUtils.ToArgbHex(ColorUtils.Darken(dominant, 0.45f).WithAlpha(secondaryAlpha));
            }

            return derived;
        }

        // RenderCanvas
        // Draws the base canvas bitmap onto the surface.
        protected virtual void RenderCanvas(SKCanvas skCanvas, SKBitmap canvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(skCanvas);
            skCanvas.DrawBitmap(canvas, 0, 0);
        }

        // CenterInSafeArea
        // Puts a band of the given height in the middle of the safe area. A style whose focal
        // element is the composition — lettering cut out of the image, a run of brush strokes —
        // sits it here, so moving the text does not drag the artwork around with it.
        protected static SKRect CenterInSafeArea(SKRect safeArea, float bandHeight)
        {
            var clamped = Math.Min(bandHeight, safeArea.Height);
            return SKRect.Create(safeArea.Left, safeArea.MidY - (clamped / 2f), safeArea.Width, clamped);
        }

        // FocalBandHeight
        // How much room the focal element gets once the text zone is reserved, never letting it
        // fall below the floor a style sets for itself.
        protected static float FocalBandHeight(SKRect safeArea, float remainingHeight, float minimumRatio)
        {
            return Math.Max(remainingHeight, safeArea.Height * minimumRatio);
        }

        // RenderGraphics
        // Loads and draws a static graphic image within the safe area.
        protected virtual void RenderGraphics(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            if (string.IsNullOrEmpty(settings.GraphicPath))
                return;

            if (!File.Exists(settings.GraphicPath))
            {
                LogError(new FileNotFoundException("Graphic file not found"), settings.GraphicPath);
                return;
            }

            try
            {
                using var stream = File.OpenRead(settings.GraphicPath);
                using var graphicBitmap = SKBitmap.Decode(stream);
                if (graphicBitmap == null)
                    return;

                ApplySafeAreaConstraints(width, height, settings, out float safeWidth, out float safeHeight, out float safeLeft, out float safeTop);
                var graphicRect = CalculateGraphicRect(graphicBitmap, width, height, safeLeft, safeTop, safeWidth, safeHeight, settings);

                using var graphicPaint = new SKPaint { IsAntialias = true };
                PaintFactory.DrawBitmap(skCanvas, graphicBitmap, graphicRect, graphicPaint);
            }
            catch
            {
                LogError(new InvalidDataException("Failed to load or render graphic"), settings.GraphicPath);
            }
        }

        // RenderTypography
        // Draws text elements on the poster.
        protected abstract void RenderTypography(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height);

        // GetSafeAreaBounds
        // Returns the safe area as an SKRect for the given poster dimensions.
        protected static SKRect GetSafeAreaBounds(int width, int height, PosterSettings settings)
        {
            ApplySafeAreaConstraints(width, height, settings, out float safeWidth, out float safeHeight, out float safeLeft, out float safeTop);
            return new SKRect(safeLeft, safeTop, safeLeft + safeWidth, safeTop + safeHeight);
        }

        // LogError
        // Reports a failed render. The style names itself, so this is written once rather than
        // twelve times with one word changed.
        protected void LogError(Exception ex, string? itemName)
        {
            Logger.LogError(ex, "Failed to generate {Style} artwork for {Item}", Style, itemName);
        }

        // CalculateGraphicRect
        // Calculates the destination rectangle for a graphic while preserving aspect ratio.
        protected virtual SKRect CalculateGraphicRect(SKBitmap graphicBitmap, int posterWidth, int posterHeight, float safeLeft, float safeTop, float safeWidth, float safeHeight, PosterSettings settings)
        {
            ArgumentNullException.ThrowIfNull(graphicBitmap);
            ArgumentNullException.ThrowIfNull(settings);

            // One size, measured from the short side like every other size, and the graphic is
            // fitted inside a box that size. Two independent percentages could stretch it and made
            // the same design look different in each shape.
            var box = SizeUnit(posterWidth, posterHeight) * (Math.Max(1f, settings.GraphicSize) / 100f);
            var originalAspect = (float)graphicBitmap.Width / graphicBitmap.Height;

            float finalWidth = originalAspect >= 1f ? box : box * originalAspect;
            float finalHeight = originalAspect >= 1f ? box / originalAspect : box;

            var x = CalculateGraphicX(settings.GraphicAlignment, safeLeft, safeWidth, finalWidth);
            var y = CalculateGraphicY(settings.GraphicPosition, safeTop, safeHeight, finalHeight);

            return new SKRect(x, y, x + finalWidth, y + finalHeight);
        }

        // CalculateGraphicX
        // Calculates the horizontal position for a graphic based on alignment.
        private float CalculateGraphicX(Alignment alignment, float safeLeft, float safeWidth, float graphicWidth)
        {
            return alignment switch
            {
                Alignment.Left => safeLeft,
                Alignment.Center => safeLeft + (safeWidth - graphicWidth) / 2f,
                Alignment.Right => safeLeft + safeWidth - graphicWidth,
                _ => safeLeft + (safeWidth - graphicWidth) / 2f
            };
        }

        // CalculateGraphicY
        // Calculates the vertical position for a graphic based on position.
        private float CalculateGraphicY(Position position, float safeTop, float safeHeight, float graphicHeight)
        {
            return position switch
            {
                Position.Top => safeTop,
                Position.Center => safeTop + (safeHeight - graphicHeight) / 2f,
                Position.Bottom => safeTop + safeHeight - graphicHeight,
                _ => safeTop + (safeHeight - graphicHeight) / 2f
            };
        }

    }
}
