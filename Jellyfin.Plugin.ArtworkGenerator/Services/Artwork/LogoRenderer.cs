using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Artwork
{
    /// <summary>
    /// Draws a transparent text logo from a series name, filled with a color or with a photo.
    /// </summary>
    public class LogoRenderer
    {
        // Share of the canvas the text may fill, leaving a little air around it.
        private const float WidthFill = 0.94f;
        private const float HeightFill = 0.86f;

        // Two lines are only worth it when they let the text grow noticeably. A short name kept on
        // one line reads as a wordmark; splitting it just to gain a few percent looks accidental.
        private const float TwoLinePreference = 1.3f;

        // Probe size for measuring. Text metrics scale linearly, so one measurement sizes any layout.
        private const float ProbeSize = 100f;

        // Gap between stacked lines, as a share of the larger line's size. Measured between the
        // letters themselves, so it is the gap the eye actually sees.
        private const float LineGap = 0.18f;

        // A logo has to read over dark backdrops, and a frame straight from an episode is usually
        // too dark for that once it is cut down to the inside of the letters.
        // The lift is a color matrix offset, which Skia measures from 0 to 1, not 0 to 255.
        private const float PhotoBrightness = 1.25f;
        private const float PhotoLift = 0.06f;

        // Brightness floor for sampled colors, as an HSV value from 0 to 100, so a logo sampled
        // from a dark poster is still legible over dark backdrops.
        private const float MinimumSampledValue = 60f;

        private readonly ILogger<LogoRenderer> _logger;

        public LogoRenderer(ILogger<LogoRenderer> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Renders the logo for a subject as PNG bytes, or null when there is no text to draw. A
        /// photo fill uses <paramref name="photo"/>, and falls back to the color when it is null.
        /// </summary>
        public byte[]? Render(ArtworkSubject subject, LogoSettings settings, SKBitmap? photo = null)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var text = LogoText.Compose(subject, settings);
            if (string.IsNullOrWhiteSpace(text.Main))
            {
                return null;
            }

            try
            {
                int width = Math.Clamp(settings.Width, 100, 4000);
                int height = Math.Clamp(settings.Height, 40, 2000);
                float secondaryScale = Math.Clamp(settings.SecondarySize, 20f, 90f) / 100f;

                var typeface = FontUtils.ResolveTypeface(settings.EffectiveFontPath, settings.FontFamily, FontUtils.GetFontStyle(settings.FontStyle));
                var lines = Layout(text, typeface, width * WidthFill, height * HeightFill, settings.MaxLines, secondaryScale);

                using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);

                var glyphs = BuildGlyphs(lines, typeface, width, height);
                try
                {
                    DrawLogo(canvas, glyphs, subject, settings, photo);
                }
                finally
                {
                    foreach (var glyph in glyphs)
                    {
                        glyph.Path.Dispose();
                    }
                }

                using var image = surface.Snapshot();

                // Width and height are the room the lettering is laid out in, not the size of the
                // file: a clear logo is expected to be trimmed to its artwork, so the empty margin
                // the layout left over is cut away before the PNG is written.
                using var trimmed = TrimToContent(image);
                using var data = (trimmed ?? image).Encode(SKEncodedImageFormat.Png, 100);
                return data?.ToArray();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogError(ex, "Failed to render logo for {SeriesName}", subject.SeriesName);
                return null;
            }
        }

        // TrimToContent
        // Crops away fully transparent edges, leaving a couple of pixels so an antialiased stroke
        // is not shaved. Returns null when there is nothing to trim, or nothing drawn at all.
        private static SKImage? TrimToContent(SKImage image)
        {
            using var pixmap = image.PeekPixels();
            if (pixmap == null)
            {
                return null;
            }

            var pixels = pixmap.GetPixelSpan();
            int rowBytes = pixmap.RowBytes;
            int left = image.Width, right = -1, top = image.Height, bottom = -1;

            for (int y = 0; y < image.Height; y++)
            {
                int row = y * rowBytes;
                for (int x = 0; x < image.Width; x++)
                {
                    // RGBA8888: alpha is the fourth byte of each pixel.
                    if (pixels[row + (x * 4) + 3] == 0)
                    {
                        continue;
                    }

                    if (x < left) left = x;
                    if (x > right) right = x;
                    if (y < top) top = y;
                    bottom = y;
                }
            }

            if (right < 0 || bottom < 0)
            {
                return null;
            }

            const int Bleed = 2;
            var crop = new SKRectI(
                Math.Max(0, left - Bleed),
                Math.Max(0, top - Bleed),
                Math.Min(image.Width, right + 1 + Bleed),
                Math.Min(image.Height, bottom + 1 + Bleed));

            if (crop.Width >= image.Width && crop.Height >= image.Height)
            {
                return null;
            }

            return image.Subset(crop);
        }

        // Layout
        // Sizes the logo's lines and puts them in drawing order. The large line takes one line or a
        // balanced two-line split; the small line, when there is one, is a fixed share of the large
        // line's size and shrinks on its own when it is too wide, rather than shrinking the large
        // line with it.
        internal static IReadOnlyList<LogoLine> Layout(LogoLines text, SKTypeface typeface, float maxWidth, float maxHeight, int maxLines, float secondaryScale)
        {
            using var probe = PaintFactory.CreateFont(typeface, ProbeSize);
            var metrics = probe.Metrics;
            float lineBox = (-metrics.Ascent + metrics.Descent) / ProbeSize;

            var secondary = string.IsNullOrWhiteSpace(text.Secondary) ? null : text.Secondary;

            // The height the small line and its gap add, measured at the probe size.
            float secondaryExtra = secondary == null
                ? 0f
                : (LineGap + (lineBox * secondaryScale)) * ProbeSize;

            var mainLines = ChooseMainLines(text.Main, probe, maxWidth, maxHeight, maxLines, secondaryExtra, out float size);

            float secondarySize = 0f;
            if (secondary != null)
            {
                secondarySize = size * secondaryScale;
                float secondaryWidth = probe.MeasureText(secondary);
                if (secondaryWidth > 0f)
                {
                    secondarySize = Math.Min(secondarySize, maxWidth / secondaryWidth * ProbeSize);
                }
            }

            // Only the order and the sizes are decided here. The lines are stacked when they are
            // drawn, from the letters' own outlines.
            var lines = new List<LogoLine>(mainLines.Length + 1);

            if (secondary != null && text.SecondaryFirst)
            {
                lines.Add(new LogoLine(secondary, secondarySize));
            }

            foreach (var main in mainLines)
            {
                lines.Add(new LogoLine(main, size));
            }

            if (secondary != null && !text.SecondaryFirst)
            {
                lines.Add(new LogoLine(secondary, secondarySize));
            }

            return lines;
        }

        // ChooseLayout
        // Returns the lines to draw and the largest font size at which they fit, choosing between
        // one line and a balanced two-line split.
        internal static IReadOnlyList<string> ChooseLayout(string text, SKTypeface typeface, float maxWidth, float maxHeight, int maxLines, out float fontSize)
        {
            using var probe = PaintFactory.CreateFont(typeface, ProbeSize);
            return ChooseMainLines(text, probe, maxWidth, maxHeight, maxLines, 0f, out fontSize);
        }

        // ChooseMainLines
        // One line or a balanced two-line split of the large text, whichever lets it be larger by a
        // clear margin. extraHeight is room reserved for the small line, at the probe size.
        private static string[] ChooseMainLines(string text, SKFont probe, float maxWidth, float maxHeight, int maxLines, float extraHeight, out float fontSize)
        {
            var best = new[] { text };
            float bestSize = FitSize(best, probe, maxWidth, maxHeight, extraHeight);
            fontSize = bestSize;

            if (maxLines < 2 || !text.Contains(' ', StringComparison.Ordinal))
            {
                return best;
            }

            // Each extra line only earns its place when it lets the lettering grow noticeably, so a
            // name is not broken up for the sake of using the allowance.
            for (var lines = 2; lines <= maxLines; lines++)
            {
                var candidate = TextUtils.SplitIntoLines(text, probe, lines).ToArray();
                if (candidate.Length < lines)
                {
                    break;
                }

                var size = FitSize(candidate, probe, maxWidth, maxHeight, extraHeight);
                if (size <= bestSize * TwoLinePreference)
                {
                    continue;
                }

                best = candidate;
                bestSize = size;
                fontSize = size;
            }

            return best;
        }

        // FitSize
        // The largest font size at which every line fits the width and the run, plus any reserved
        // height, fits the height. Everything scales linearly with size, so this is a division.
        private static float FitSize(string[] lines, SKFont probe, float maxWidth, float maxHeight, float extraHeight)
        {
            var metrics = probe.Metrics;
            float lineBox = -metrics.Ascent + metrics.Descent;
            float blockAtProbe = lineBox + ((lines.Length - 1) * ProbeSize * RenderConstants.LineHeightMultiplier) + extraHeight;
            float widestAtProbe = lines.Max(line => probe.MeasureText(line));

            if (widestAtProbe <= 0f || blockAtProbe <= 0f)
            {
                return ProbeSize;
            }

            float byWidth = maxWidth / widestAtProbe * ProbeSize;
            float byHeight = maxHeight / blockAtProbe * ProbeSize;
            return Math.Max(4f, Math.Min(byWidth, byHeight));
        }

        // BuildGlyphs
        // Turns each line into an outline path, then stacks and centers the run by the letters' ink
        // rather than their em boxes. A line of capitals leaves a lot of empty space above its
        // baseline, which as an em box opens a gap that grows with the font size and sits the whole
        // run low on the canvas.
        private static List<LogoGlyphs> BuildGlyphs(IReadOnlyList<LogoLine> lines, SKTypeface typeface, int width, int height)
        {
            var glyphs = new List<LogoGlyphs>(lines.Count);

            foreach (var line in lines)
            {
                using var font = PaintFactory.CreateFont(typeface, line.Size);
                float advance = font.MeasureText(line.Text);
                glyphs.Add(new LogoGlyphs(font.GetTextPath(line.Text, new SKPoint(-advance / 2f, 0f)), line.Size));
            }

            var bounds = SKRect.Empty;
            float inkBottom = 0f;

            for (int i = 0; i < glyphs.Count; i++)
            {
                var path = glyphs[i].Path;
                var ink = path.Bounds;
                if (ink.IsEmpty)
                {
                    continue;
                }

                if (!bounds.IsEmpty)
                {
                    path.Offset(0f, inkBottom + (LineGap * Math.Max(glyphs[i - 1].Size, glyphs[i].Size)) - ink.Top);
                    ink = path.Bounds;
                }

                inkBottom = ink.Bottom;
                bounds = bounds.IsEmpty ? ink : SKRect.Union(bounds, ink);
            }

            float dx = (width / 2f) - bounds.MidX;
            float dy = (height / 2f) - bounds.MidY;
            foreach (var glyph in glyphs)
            {
                glyph.Path.Offset(dx, dy);
            }

            return glyphs;
        }

        // DrawLogo
        // Shadow, then outline, then fill. The fill covers the inner half of the outline stroke,
        // leaving a clean border outside the letter edge.
        private void DrawLogo(SKCanvas canvas, List<LogoGlyphs> glyphs, ArtworkSubject subject, LogoSettings settings, SKBitmap? photo)
        {
            if (settings.ShadowEnabled)
            {
                foreach (var glyph in glyphs)
                {
                    using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(1f, glyph.Size * 0.03f));
                    using var shadow = new SKPaint { Color = RenderConstants.ShadowColor, IsAntialias = true, Style = SKPaintStyle.Fill, MaskFilter = blur };
                    float offset = Math.Max(1f, glyph.Size * 0.04f);

                    canvas.Save();
                    canvas.Translate(offset, offset);
                    canvas.DrawPath(glyph.Path, shadow);
                    canvas.Restore();
                }
            }

            if (settings.OutlineEnabled)
            {
                var outlineColor = ColorUtils.ParseHexColor(settings.OutlineColor);
                foreach (var glyph in glyphs)
                {
                    using var outline = new SKPaint
                    {
                        Color = outlineColor,
                        Style = SKPaintStyle.Stroke,
                        StrokeWidth = Math.Max(1f, glyph.Size * (Math.Max(0f, settings.OutlineWidth) / 100f)),
                        StrokeJoin = SKStrokeJoin.Round,
                        IsAntialias = true
                    };
                    canvas.DrawPath(glyph.Path, outline);
                }
            }

            if (settings.Fill == LogoFill.Photo && photo != null && FillWithPhoto(canvas, glyphs, photo))
            {
                return;
            }

            using var fill = new SKPaint { Color = ResolveColor(subject, settings), IsAntialias = true, Style = SKPaintStyle.Fill };
            foreach (var glyph in glyphs)
            {
                canvas.DrawPath(glyph.Path, fill);
            }
        }

        // FillWithPhoto
        // Clips to the letters and draws the photo through them. The photo covers the letters'
        // bounds rather than the whole canvas, so the picture fills the letters instead of mostly
        // falling outside them.
        private static bool FillWithPhoto(SKCanvas canvas, List<LogoGlyphs> glyphs, SKBitmap photo)
        {
            if (photo.Width <= 0 || photo.Height <= 0)
            {
                return false;
            }

            using var letters = new SKPath();
            foreach (var glyph in glyphs)
            {
                letters.AddPath(glyph.Path);
            }

            var bounds = letters.Bounds;
            if (bounds.IsEmpty)
            {
                return false;
            }

            float scale = Math.Max(bounds.Width / photo.Width, bounds.Height / photo.Height);
            float drawWidth = photo.Width * scale;
            float drawHeight = photo.Height * scale;
            var dest = SKRect.Create(bounds.MidX - (drawWidth / 2f), bounds.MidY - (drawHeight / 2f), drawWidth, drawHeight);

            using var image = SKImage.FromBitmap(photo);
            if (image == null)
            {
                return false;
            }

            using var brighten = SKColorFilter.CreateColorMatrix(new[]
            {
                PhotoBrightness, 0f, 0f, 0f, PhotoLift,
                0f, PhotoBrightness, 0f, 0f, PhotoLift,
                0f, 0f, PhotoBrightness, 0f, PhotoLift,
                0f, 0f, 0f, 1f, 0f
            });
            using var paint = new SKPaint { ColorFilter = brighten, IsAntialias = true };

            canvas.Save();
            canvas.ClipPath(letters, SKClipOperation.Intersect, true);
            canvas.DrawImage(image, dest, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
            canvas.Restore();
            return true;
        }

        // ResolveColor
        // The configured color, or the dominant color of the chosen series artwork lifted to a
        // legible brightness. The configured alpha applies either way.
        private SKColor ResolveColor(ArtworkSubject subject, LogoSettings settings)
        {
            var configured = ColorUtils.ParseHexColor(settings.Color);
            if (settings.ColorSource == LogoColorSource.Fixed)
            {
                return configured;
            }

            var path = settings.ColorSource == LogoColorSource.SeriesPoster
                ? subject.VideoMetadata.SeriesPosterFilePath
                : subject.VideoMetadata.SeriesBackdropFilePath;

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return configured;
            }

            try
            {
                using var bitmap = SKBitmap.Decode(path);
                if (bitmap == null)
                {
                    return configured;
                }

                var dominant = ColorUtils.GetDominantColor(bitmap);
                if (dominant == SKColor.Empty)
                {
                    return configured;
                }

                return ColorUtils.EnsureMinimumBrightness(dominant, MinimumSampledValue).WithAlpha(configured.Alpha);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Could not sample logo color from {Path}", path);
                return configured;
            }
        }

        /// <summary>One line of a logo layout: its text and font size, in drawing order.</summary>
        internal readonly record struct LogoLine(string Text, float Size);

        // One line's outline path and the font size it was built at, which sizes its stroke and shadow.
        private readonly record struct LogoGlyphs(SKPath Path, float Size);
    }
}
