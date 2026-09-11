using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Utilities;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Services.Artwork
{
    /// <summary>
    /// Draws a transparent text logo from a series name, filled with a colour or with a photo.
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

        // Gap between the large and the small line, as a share of the large line's size.
        private const float SecondaryGap = 0.12f;

        // A logo has to read over dark backdrops, and a frame straight from an episode is usually
        // too dark for that once it is cut down to the inside of the letters.
        // The lift is a colour matrix offset, which Skia measures from 0 to 1, not 0 to 255.
        private const float PhotoBrightness = 1.25f;
        private const float PhotoLift = 0.06f;

        // Brightness floor for sampled colours, as an HSV value from 0 to 100, so a logo sampled
        // from a dark poster is still legible over dark backdrops.
        private const float MinimumSampledValue = 60f;

        private readonly ILogger<LogoRenderer> _logger;

        public LogoRenderer(ILogger<LogoRenderer> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Renders the logo for a subject as PNG bytes, or null when there is no text to draw. A
        /// photo fill uses <paramref name="photo"/>, and falls back to the colour when it is null.
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
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                return data?.ToArray();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogError(ex, "Failed to render logo for {SeriesName}", subject.SeriesName);
                return null;
            }
        }

        // Layout
        // Sizes and stacks the logo's lines. The large line takes one line or a balanced two-line
        // split; the small line, when there is one, is a fixed share of the large line's size and
        // shrinks on its own when it is too wide, rather than shrinking the large line with it.
        // Baselines are measured from the first line's baseline.
        internal static IReadOnlyList<LogoLine> Layout(LogoLines text, SKTypeface typeface, float maxWidth, float maxHeight, int maxLines, float secondaryScale)
        {
            using var probe = PaintFactory.CreateFont(typeface, ProbeSize);
            var metrics = probe.Metrics;
            float ascent = -metrics.Ascent / ProbeSize;
            float descent = metrics.Descent / ProbeSize;

            var secondary = string.IsNullOrWhiteSpace(text.Secondary) ? null : text.Secondary;

            // The height the small line and its gap add, measured at the probe size.
            float secondaryExtra = secondary == null
                ? 0f
                : (SecondaryGap + ((ascent + descent) * secondaryScale)) * ProbeSize;

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

            var lines = new List<LogoLine>(mainLines.Length + 1);
            float gap = SecondaryGap * size;
            float baseline = 0f;

            if (secondary != null && text.SecondaryFirst)
            {
                lines.Add(new LogoLine(secondary, secondarySize, baseline));
                baseline += (descent * secondarySize) + gap + (ascent * size);
            }

            for (int i = 0; i < mainLines.Length; i++)
            {
                if (i > 0)
                {
                    baseline += size * RenderConstants.LineHeightMultiplier;
                }

                lines.Add(new LogoLine(mainLines[i], size, baseline));
            }

            if (secondary != null && !text.SecondaryFirst)
            {
                baseline += (descent * size) + gap + (ascent * secondarySize);
                lines.Add(new LogoLine(secondary, secondarySize, baseline));
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
            var single = new[] { text };
            float singleSize = FitSize(single, probe, maxWidth, maxHeight, extraHeight);

            if (maxLines < 2 || !text.Contains(' ', StringComparison.Ordinal))
            {
                fontSize = singleSize;
                return single;
            }

            var (first, second) = TextUtils.SplitBalanced(text, probe);
            if (string.IsNullOrEmpty(second))
            {
                fontSize = singleSize;
                return single;
            }

            var split = new[] { first, second };
            float splitSize = FitSize(split, probe, maxWidth, maxHeight, extraHeight);

            if (splitSize > singleSize * TwoLinePreference)
            {
                fontSize = splitSize;
                return split;
            }

            fontSize = singleSize;
            return single;
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
        // Turns each line into an outline path, centred on the canvas by its ink rather than its
        // em box, so capitals sit visually centred instead of low.
        private static List<LogoGlyphs> BuildGlyphs(IReadOnlyList<LogoLine> lines, SKTypeface typeface, int width, int height)
        {
            var glyphs = new List<LogoGlyphs>(lines.Count);
            var bounds = SKRect.Empty;

            foreach (var line in lines)
            {
                using var font = PaintFactory.CreateFont(typeface, line.Size);
                float advance = font.MeasureText(line.Text);
                var path = font.GetTextPath(line.Text, new SKPoint(-advance / 2f, line.Baseline));
                glyphs.Add(new LogoGlyphs(path, line.Size));

                var pathBounds = path.Bounds;
                if (!pathBounds.IsEmpty)
                {
                    bounds = bounds.IsEmpty ? pathBounds : SKRect.Union(bounds, pathBounds);
                }
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
        // The configured colour, or the dominant colour of the chosen series artwork lifted to a
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
                _logger.LogDebug(ex, "Could not sample logo colour from {Path}", path);
                return configured;
            }
        }

        /// <summary>One line of a logo layout: its text, font size, and baseline.</summary>
        internal readonly record struct LogoLine(string Text, float Size, float Baseline);

        // One line's outline path and the font size it was built at, which sizes its stroke and shadow.
        private readonly record struct LogoGlyphs(SKPath Path, float Size);
    }
}
