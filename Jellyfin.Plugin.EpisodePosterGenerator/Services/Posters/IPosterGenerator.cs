using System;
using System.Globalization;
using System.IO;
using SkiaSharp;
using Jellyfin.Plugin.EpisodePosterGenerator.Configuration;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Utilities;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Services.Posters
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

        // SupportedShapes
        // The poster shapes this style can lay out.
        ArtworkShapes SupportedShapes { get; }

        // Supports
        // Returns true when this style can lay out the given shape.
        bool Supports(ArtworkShape shape);
    }

    public abstract class BasePosterGenerator : IPosterGenerator
    {
        // Style
        // The poster style this generator produces.
        public abstract PosterStyle Style { get; }

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        public abstract string Description { get; }

        // SupportedShapes
        // Every style lays out both shapes unless it says otherwise.
        public virtual ArtworkShapes SupportedShapes => ArtworkShapes.All;

        // Supports
        // Returns true when this style can lay out the given shape.
        public bool Supports(ArtworkShape shape)
        {
            var flag = shape == ArtworkShape.Portrait ? ArtworkShapes.Portrait : ArtworkShapes.Landscape;
            return (SupportedShapes & flag) != 0;
        }

        // SizeUnit
        // The length every size setting is a percentage of: the poster's short edge. For a landscape
        // poster that is the height, exactly as before; for a portrait poster it is the width, so a
        // design's text keeps the same weight relative to the frame instead of overflowing it.
        protected static int SizeUnit(int width, int height) => Math.Max(1, Math.Min(width, height));

        // GetSafeAreaMargin
        // Returns the safe area margin as a percentage of the poster dimensions.
        protected static float GetSafeAreaMargin(PosterSettings settings) => settings.PosterSafeArea / 100f;

        // GetElementSpacing
        // The configured gap between stacked elements, in pixels for this poster height.
        // Every style resolves spacing through here so one setting moves them all consistently.
        protected static float GetElementSpacing(PosterSettings settings, float posterHeight)
            => posterHeight * (Math.Max(0f, settings.ElementSpacing) / 100f);

        // Layout block keys shared by the styles that stack text against the bottom edge.
        protected const string EpisodeBlock = "episode";
        protected const string SeparatorBlock = "separator";
        protected const string TitleBlock = "title";

        // CreateTitleStyle
        // The font and paints for the episode title, sized and coloured from the settings.
        protected static TextStyle CreateTitleStyle(PosterSettings settings, int height, SKTextAlign align = SKTextAlign.Center, bool withShadow = true)
        {
            ArgumentNullException.ThrowIfNull(settings);

            var fontSize = FontUtils.CalculateFontSizeFromPercentage(settings.TitleFontSize, height);
            var typeface = FontUtils.ResolveTypeface(settings.EffectiveTitleFontPath, settings.TitleFontFamily, FontUtils.GetFontStyle(settings.TitleFontStyle));
            return PaintFactory.CreateTextStyle(ColorUtils.ParseHexColor(settings.TitleFontColor), fontSize, typeface, height, align, withShadow);
        }

        // CreateEpisodeStyle
        // The font and paints for the episode code or number, sized and coloured from the settings.
        protected static TextStyle CreateEpisodeStyle(PosterSettings settings, int height, SKTextAlign align = SKTextAlign.Center, bool withShadow = true)
        {
            ArgumentNullException.ThrowIfNull(settings);

            var fontSize = FontUtils.CalculateFontSizeFromPercentage(settings.EpisodeFontSize, height);
            var typeface = ResolveEpisodeTypeface(settings, FontUtils.GetFontStyle(settings.EpisodeFontStyle));
            return PaintFactory.CreateTextStyle(ColorUtils.ParseHexColor(settings.EpisodeFontColor), fontSize, typeface, height, align, withShadow);
        }

        // ResolveEpisodeTypeface
        // The episode face in the given weight. Not disposed by callers: FontUtils owns the cache.
        protected static SKTypeface ResolveEpisodeTypeface(PosterSettings settings, SKFontStyle style)
        {
            ArgumentNullException.ThrowIfNull(settings);
            return FontUtils.ResolveTypeface(settings.EffectiveEpisodeFontPath, settings.EpisodeFontFamily, style);
        }

        // DrawTitleInSlot
        // Fits the title to the slot's height and the given width, centres the resulting lines
        // vertically in the slot, and draws them at x. Returns the number of lines drawn, which
        // is zero when the long title handling drops the title.
        //
        // Title slots are reserved at a fixed two lines so the elements above them cannot shift
        // between episodes with short and long titles. A one line title therefore leaves a line
        // of slack, and centring splits it evenly rather than pooling it at one end.
        protected static int DrawTitleInSlot(SKCanvas canvas, string title, TextStyle style, SKRect slot, float x, float maxWidth, LongTitleHandling handling)
        {
            ArgumentNullException.ThrowIfNull(style);

            var lines = TextUtils.FitTitleLines(title, style.Font, maxWidth, slot.Height, style.LineHeight, handling);
            if (lines.Count == 0)
            {
                return 0;
            }

            style.DrawLines(canvas, lines, x, style.FirstBaselineCentered(slot, lines.Count));
            return lines.Count;
        }

        // DrawBottomTextStack
        // The layout Standard and Split share: an identity line, a rule, and a two line title zone
        // packed against the bottom of the area. An episode's identity line is its season and
        // episode numbers; a season or series draws its label, such as SEASON 2, instead.
        protected static void DrawBottomTextStack(SKCanvas canvas, SKRect area, ArtworkSubject subject, PosterSettings settings, int unit)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            using var titleStyle = CreateTitleStyle(settings, unit);
            using var episodeStyle = CreateEpisodeStyle(settings, unit);

            var parts = subject.NumberParts;
            var label = subject.Label;
            var showEpisode = settings.ShowEpisode && (parts.Count > 1 || label.Length > 0);
            var showSeparator = settings.ShowTitle && showEpisode;

            var column = new LayoutColumn(area, GetElementSpacing(settings, unit), LayoutAnchor.Bottom)
                .Add(EpisodeBlock, showEpisode ? episodeStyle.LineBox : 0f)
                .Add(SeparatorBlock, showSeparator ? RenderConstants.SeparatorSlotHeight(unit) : 0f)
                .Add(TitleBlock, settings.ShowTitle ? titleStyle.BlockHeight(2) : 0f);

            if (column.TryGetSlot(EpisodeBlock, out var episodeSlot))
            {
                if (parts.Count > 1)
                {
                    DrawSeasonEpisodeInfo(canvas, parts[0], parts[1], episodeStyle, settings, unit, episodeSlot);
                }
                else
                {
                    episodeStyle.Draw(canvas, label, episodeSlot.MidX, episodeStyle.BaselineAtBottom(episodeSlot));
                }
            }

            if (column.TryGetSlot(SeparatorBlock, out var separatorSlot))
            {
                DrawSeparatorLine(canvas, settings, unit, separatorSlot);
            }

            if (column.TryGetSlot(TitleBlock, out var titleSlot))
            {
                DrawTitleInSlot(canvas, subject.Title ?? "-", titleStyle, titleSlot, titleSlot.MidX, titleSlot.Width * RenderConstants.TextWidthMultiplier, settings.LongTitleHandling);
            }
        }

        // DrawSeasonEpisodeInfo
        // Draws "season • episode" centred on the slot's bottom edge. The bullet uses the
        // regular weight so it does not overpower the numbers beside it.
        protected static void DrawSeasonEpisodeInfo(SKCanvas canvas, int seasonNumber, int episodeNumber, TextStyle episodeStyle, PosterSettings settings, int height, SKRect slot)
        {
            ArgumentNullException.ThrowIfNull(episodeStyle);
            ArgumentNullException.ThrowIfNull(settings);

            using var bulletStyle = PaintFactory.CreateTextStyle(
                episodeStyle.Fill.Color, episodeStyle.Size, ResolveEpisodeTypeface(settings, SKFontStyle.Normal), height);

            var seasonText = seasonNumber.ToString(CultureInfo.InvariantCulture);
            var episodeText = episodeNumber.ToString(CultureInfo.InvariantCulture);
            const string bulletText = " • ";

            var baselineY = episodeStyle.BaselineAtBottom(slot);

            var seasonWidth = episodeStyle.MeasureWidth(seasonText);
            var episodeWidth = episodeStyle.MeasureWidth(episodeText);
            var bulletWidth = bulletStyle.MeasureWidth(bulletText);

            var bulletX = slot.MidX;
            episodeStyle.Draw(canvas, seasonText, bulletX - (bulletWidth / 2f) - (seasonWidth / 2f), baselineY);
            bulletStyle.Draw(canvas, bulletText, bulletX, baselineY);
            episodeStyle.Draw(canvas, episodeText, bulletX + (bulletWidth / 2f) + (episodeWidth / 2f), baselineY);
        }

        // DrawSeparatorLine
        // Draws a horizontal rule across the slot in the episode colour, with a shadow.
        protected static void DrawSeparatorLine(SKCanvas canvas, PosterSettings settings, int height, SKRect slot)
        {
            ArgumentNullException.ThrowIfNull(settings);

            var stroke = RenderConstants.SeparatorStrokeWidth(height);
            using var shadowPaint = PaintFactory.CreateShadowLinePaint(stroke);
            using var linePaint = PaintFactory.CreateLinePaint(ColorUtils.ParseHexColor(settings.EpisodeFontColor), stroke);

            PaintFactory.DrawLineWithShadow(canvas, slot.Left, slot.MidY, slot.Right, slot.MidY, linePaint, shadowPaint, RenderConstants.ShadowOffset(height));
        }

        // ApplySafeAreaConstraints
        // Calculates the safe area dimensions and offsets for a given poster size.
        // The margin is the safe area percent of the poster's short edge, applied as the same
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

        // RenderOverlay
        // Applies a color overlay with optional gradient to the poster.
        protected virtual void RenderOverlay(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            if (string.IsNullOrEmpty(settings.OverlayColor))
                return;

            var primaryColor = ColorUtils.ParseHexColor(settings.OverlayColor);
            if (primaryColor.Alpha == 0)
                return;

            var rect = SKRect.Create(width, height);

            // Solid color overlay branch
            if (settings.OverlayGradient == OverlayGradient.None)
            {
                using var overlayPaint = new SKPaint
                {
                    Color = primaryColor,
                    Style = SKPaintStyle.Fill
                };
                skCanvas.DrawRect(rect, overlayPaint);
            }
            // Gradient overlay branch
            else
            {
                var secondaryColor = ColorUtils.ParseHexColor(settings.OverlaySecondaryColor);
                if (secondaryColor.Alpha == 0) secondaryColor = primaryColor;

                // SKPaint does not own its shader, so the gradient is disposed here rather
                // than left to the finalizer — a full library run creates one per poster.
                using var gradient = CreateOverlayGradient(settings.OverlayGradient, rect, primaryColor, secondaryColor);
                if (gradient != null)
                {
                    using var overlayPaint = new SKPaint
                    {
                        Shader = gradient,
                        Style = SKPaintStyle.Fill,
                        IsDither = true
                    };
                    skCanvas.DrawRect(rect, overlayPaint);
                }
            }
        }

        // CreateOverlayGradient
        // Creates a shader for the specified gradient direction.
        protected virtual SKShader? CreateOverlayGradient(OverlayGradient gradientType, SKRect rect, SKColor primaryColor, SKColor secondaryColor)
        {
            var colors = new[] { primaryColor, secondaryColor };

            return gradientType switch
            {
                OverlayGradient.LeftToRight => SKShader.CreateLinearGradient(
                    new SKPoint(rect.Left, rect.MidY),
                    new SKPoint(rect.Right, rect.MidY),
                    colors, null, SKShaderTileMode.Clamp, SKMatrix.Identity),

                OverlayGradient.BottomToTop => SKShader.CreateLinearGradient(
                    new SKPoint(rect.MidX, rect.Bottom),
                    new SKPoint(rect.MidX, rect.Top),
                    colors, null, SKShaderTileMode.Clamp, SKMatrix.Identity),

                OverlayGradient.TopLeftCornerToBottomRightCorner => SKShader.CreateLinearGradient(
                    new SKPoint(rect.Left, rect.Top),
                    new SKPoint(rect.Right, rect.Bottom),
                    colors, null, SKShaderTileMode.Clamp, SKMatrix.Identity),

                OverlayGradient.TopRightCornerToBottomLeftCorner => SKShader.CreateLinearGradient(
                    new SKPoint(rect.Right, rect.Top),
                    new SKPoint(rect.Left, rect.Bottom),
                    colors, null, SKShaderTileMode.Clamp, SKMatrix.Identity),

                _ => null
            };
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
        // Logs an error that occurred during poster generation.
        protected abstract void LogError(Exception ex, string? episodeName);

        // CalculateGraphicRect
        // Calculates the destination rectangle for a graphic while preserving aspect ratio.
        protected virtual SKRect CalculateGraphicRect(SKBitmap graphicBitmap, int posterWidth, int posterHeight, float safeLeft, float safeTop, float safeWidth, float safeHeight, PosterSettings settings)
        {
            ArgumentNullException.ThrowIfNull(graphicBitmap);
            ArgumentNullException.ThrowIfNull(settings);

            var maxWidth = posterWidth * (settings.GraphicWidth / 100f);
            var maxHeight = posterHeight * (settings.GraphicHeight / 100f);

            var originalAspect = (float)graphicBitmap.Width / graphicBitmap.Height;
            var constraintAspect = maxWidth / maxHeight;

            float finalWidth, finalHeight;

            // Image is wider than constraint - fit to width
            if (originalAspect > constraintAspect)
            {
                finalWidth = maxWidth;
                finalHeight = maxWidth / originalAspect;
            }
            // Image is taller than constraint - fit to height
            else
            {
                finalHeight = maxHeight;
                finalWidth = maxHeight * originalAspect;
            }

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
