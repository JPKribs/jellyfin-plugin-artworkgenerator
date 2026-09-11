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
    /// Draws a transparent text logo from a series name.
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

        // Brightness floor for sampled colours, as an HSV value from 0 to 100, so a logo sampled
        // from a dark poster is still legible over dark backdrops.
        private const float MinimumSampledValue = 60f;

        private readonly ILogger<LogoRenderer> _logger;

        public LogoRenderer(ILogger<LogoRenderer> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Renders the logo for a subject as PNG bytes, or null when there is no text to draw.
        /// </summary>
        public byte[]? Render(ArtworkSubject subject, LogoSettings settings)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var text = LogoText.Resolve(subject, settings);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                int width = Math.Clamp(settings.Width, 100, 4000);
                int height = Math.Clamp(settings.Height, 40, 2000);

                var typeface = FontUtils.ResolveTypeface(settings.EffectiveFontPath, settings.FontFamily, FontUtils.GetFontStyle(settings.FontStyle));
                var lines = ChooseLayout(text, typeface, width * WidthFill, height * HeightFill, settings.MaxLines, out float fontSize);
                var color = ResolveColor(subject, settings);

                using var style = CreateStyle(color, fontSize, typeface, settings.ShadowEnabled);
                using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);

                float centerX = width / 2f;
                float firstBaseline = style.FirstBaselineCentered(SKRect.Create(width, height), lines.Count);

                if (settings.OutlineEnabled)
                {
                    using var outline = new SKPaint
                    {
                        Color = ColorUtils.ParseHexColor(settings.OutlineColor),
                        Style = SKPaintStyle.Stroke,
                        StrokeWidth = Math.Max(1f, fontSize * (Math.Max(0f, settings.OutlineWidth) / 100f)),
                        StrokeJoin = SKStrokeJoin.Round,
                        IsAntialias = true
                    };

                    for (int i = 0; i < lines.Count; i++)
                    {
                        canvas.DrawText(lines[i], centerX, firstBaseline + (i * style.LineHeight), SKTextAlign.Center, style.Font, outline);
                    }
                }

                style.DrawLines(canvas, lines, centerX, firstBaseline);

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

        // ChooseLayout
        // Returns the lines to draw and the largest font size at which they fit, choosing between
        // one line and a balanced two-line split.
        internal static IReadOnlyList<string> ChooseLayout(string text, SKTypeface typeface, float maxWidth, float maxHeight, int maxLines, out float fontSize)
        {
            using var probe = PaintFactory.CreateFont(typeface, ProbeSize);

            var single = new[] { text };
            float singleSize = FitSize(single, probe, maxWidth, maxHeight);

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
            float splitSize = FitSize(split, probe, maxWidth, maxHeight);

            if (splitSize > singleSize * TwoLinePreference)
            {
                fontSize = splitSize;
                return split;
            }

            fontSize = singleSize;
            return single;
        }

        // FitSize
        // The largest font size at which every line fits the width and the run fits the height.
        // Width and height both scale linearly with size, so this is a division, not a search.
        private static float FitSize(string[] lines, SKFont probe, float maxWidth, float maxHeight)
        {
            var metrics = probe.Metrics;
            float lineBox = -metrics.Ascent + metrics.Descent;
            float blockAtProbe = lineBox + ((lines.Length - 1) * ProbeSize * RenderConstants.LineHeightMultiplier);
            float widestAtProbe = lines.Max(line => probe.MeasureText(line));

            if (widestAtProbe <= 0f || blockAtProbe <= 0f)
            {
                return ProbeSize;
            }

            float byWidth = maxWidth / widestAtProbe * ProbeSize;
            float byHeight = maxHeight / blockAtProbe * ProbeSize;
            return Math.Max(4f, Math.Min(byWidth, byHeight));
        }

        // CreateStyle
        // Logo text is sized to its canvas rather than to a poster, so the shadow is scaled from
        // the font size instead of from the poster height the shared factory assumes.
        private static TextStyle CreateStyle(SKColor color, float fontSize, SKTypeface typeface, bool withShadow)
        {
            var font = PaintFactory.CreateFont(typeface, fontSize);
            var fill = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };

            if (!withShadow)
            {
                return new TextStyle(font, fill, null, null, SKTextAlign.Center, 0f);
            }

            var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(1f, fontSize * 0.03f));
            var shadow = new SKPaint
            {
                Color = RenderConstants.ShadowColor,
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                MaskFilter = blur
            };

            return new TextStyle(font, fill, shadow, blur, SKTextAlign.Center, Math.Max(1f, fontSize * 0.04f));
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
    }
}
