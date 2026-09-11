using System;
using System.Globalization;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Utilities;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Services.Posters
{
    public class FadePosterGenerator : BasePosterGenerator
    {
        // Portion of the poster width the overlay stays fully solid, and where it ends.
        private const float SolidStop = 0.4f;
        private const float TransparentStop = 0.75f;

        // The number occupies a fixed zone at the bottom of the safe area. Its size and
        // position derive only from the poster geometry, never from the digits or the
        // title, so the number sits at exactly the same spot on every episode.
        private const float NumberZoneHeightRatio = 0.3f;
        private const float NumberZoneWidthRatio = 0.45f;

        // A portrait poster is narrow, so the number may use more of the width.
        private const float PortraitNumberZoneWidthRatio = 0.7f;

        // A series has no number, so its name takes that corner instead: a wider, shorter zone
        // than the digits need.
        private const float FocalTitleWidthRatio = 0.9f;
        private const float FocalTitleHeightRatio = 0.32f;

        // Style
        // The poster style this generator produces.
        public override PosterStyle Style => PosterStyle.Fade;

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        public override string Description => "One sided fade with a big number and a vertical title. Editorial and bold.";

        private readonly ILogger<FadePosterGenerator> _logger;

        // FadePosterGenerator
        // Initializes a new instance of the fade poster generator with logging support.
        public FadePosterGenerator(ILogger<FadePosterGenerator> logger)
        {
            _logger = logger;
        }

        // RenderOverlay
        // Draws a hard one-sided gradient: solid overlay color on the left that holds until
        // partway across, then falls off to transparent so the right side shows the frame.
        protected override void RenderOverlay(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(skCanvas);
            ArgumentNullException.ThrowIfNull(settings);

            if (string.IsNullOrEmpty(settings.OverlayColor))
            {
                return;
            }

            var primaryColor = ColorUtils.ParseHexColor(settings.OverlayColor);
            if (primaryColor.Alpha == 0)
            {
                return;
            }

            var rect = SKRect.Create(width, height);
            var colors = new[] { primaryColor, primaryColor, primaryColor.WithAlpha(0) };
            var positions = new[] { 0f, SolidStop, TransparentStop };

            using var shader = SKShader.CreateLinearGradient(
                new SKPoint(rect.Left, rect.MidY),
                new SKPoint(rect.Right, rect.MidY),
                colors,
                positions,
                SKShaderTileMode.Clamp);

            using var overlayPaint = new SKPaint
            {
                Shader = shader,
                Style = SKPaintStyle.Fill,
                IsDither = true
            };
            skCanvas.DrawRect(rect, overlayPaint);
        }

        // RenderTypography
        // Renders a large number pinned to the bottom left with the title rotated vertically along
        // the left edge above it. A series has no number, so its title runs the full height.
        protected override void RenderTypography(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var unit = SizeUnit(width, height);
            var safeArea = GetSafeAreaBounds(width, height, settings);
            float numberTop = safeArea.Bottom;

            if (settings.ShowEpisode && subject.Number.HasValue)
            {
                var widthRatio = height > width ? PortraitNumberZoneWidthRatio : NumberZoneWidthRatio;
                DrawEpisodeNumber(skCanvas, subject.Number.Value, settings, safeArea, unit, widthRatio);
                numberTop = safeArea.Bottom - (safeArea.Height * NumberZoneHeightRatio);
            }

            if (settings.ShowTitle && !string.IsNullOrEmpty(subject.Title))
            {
                // Without a number there is nothing for a thin vertical title to sit above, so the
                // name becomes the focal element and takes the number's corner.
                if (subject.Number.HasValue)
                {
                    DrawVerticalTitle(skCanvas, subject.Title, settings, unit, safeArea, numberTop);
                }
                else
                {
                    DrawFocalTitle(skCanvas, subject.Title, settings, safeArea, unit);
                }
            }
        }

        // DrawFocalTitle
        // Draws the name where the number would go, sized to fill that corner and pinned to the
        // same bottom-left anchor.
        private static void DrawFocalTitle(SKCanvas canvas, string title, PosterSettings config, SKRect safeArea, int unit)
        {
            var typeface = FontUtils.ResolveTypeface(config.EffectiveTitleFontPath, config.TitleFontFamily, FontUtils.GetFontStyle(config.TitleFontStyle));

            float maxWidth = safeArea.Width * FocalTitleWidthRatio;
            float maxHeight = safeArea.Height * FocalTitleHeightRatio;
            float fontSize = FontUtils.CalculateOptimalFontSize(title, typeface, maxWidth, maxHeight);

            using var style = PaintFactory.CreateTextStyle(ColorUtils.ParseHexColor(config.TitleFontColor), fontSize, typeface, unit, SKTextAlign.Left);
            var lines = TextUtils.FitTitleLines(title, style.Font, maxWidth, config.LongTitleHandling);

            float baseline = safeArea.Bottom - ((lines.Count - 1) * style.LineHeight);
            for (int i = 0; i < lines.Count; i++)
            {
                style.Draw(canvas, lines[i], safeArea.Left, baseline + (i * style.LineHeight));
            }
        }

        // DrawEpisodeNumber
        // Draws the zero padded number at its fixed spot. The size comes from a fixed reference
        // string so the digits themselves cannot change it. Numbers with three or more digits
        // shrink to fit without moving the anchor.
        private static void DrawEpisodeNumber(SKCanvas canvas, int number, PosterSettings config, SKRect safeArea, int unit, float widthRatio)
        {
            var numberText = number.ToString("D2", CultureInfo.InvariantCulture);
            var typeface = ResolveEpisodeTypeface(config, FontUtils.GetFontStyle(config.EpisodeFontStyle));

            float maxWidth = safeArea.Width * widthRatio;
            float maxHeight = safeArea.Height * NumberZoneHeightRatio;
            float fontSize = FontUtils.CalculateOptimalFontSize("00", typeface, maxWidth, maxHeight);

            var bounds = FontUtils.MeasureTextDimensions(numberText, typeface, fontSize);
            if (bounds.Width > maxWidth)
            {
                fontSize *= maxWidth / bounds.Width;
            }

            using var style = PaintFactory.CreateTextStyle(ColorUtils.ParseHexColor(config.EpisodeFontColor), fontSize, typeface, unit, SKTextAlign.Left);
            style.Draw(canvas, numberText, safeArea.Left, safeArea.Bottom);
        }

        // DrawVerticalTitle
        // Draws the uppercase title rotated 90 degrees, running upward along the left edge
        // from just above the number. The title wraps to up to two vertical rows before the
        // long title handling engages, matching the other styles.
        private static void DrawVerticalTitle(SKCanvas canvas, string title, PosterSettings config, int unit, SKRect safeArea, float numberTop)
        {
            using var style = CreateTitleStyle(config, unit, SKTextAlign.Left);

            float spacing = GetElementSpacing(config, unit);
            float startY = numberTop - spacing;
            float availableRun = startY - safeArea.Top;
            if (availableRun <= style.Size)
            {
                return;
            }

            var lines = TextUtils.FitTitleLines(title.ToUpperInvariant(), style.Font, availableRun, config.LongTitleHandling);
            if (lines.Count == 0)
            {
                return;
            }

            // Rotate around each anchor so the text runs bottom to top along the left edge. The
            // baseline sits on the anchor's vertical line, shifted right by the ascent so glyphs
            // stay inside the safe area. The second row sits one line height further right.
            float firstAnchorX = safeArea.Left + style.Ascent;

            for (int i = 0; i < lines.Count; i++)
            {
                float anchorX = firstAnchorX + (i * style.LineHeight);
                canvas.Save();
                canvas.RotateDegrees(-90, anchorX, startY);
                style.Draw(canvas, lines[i], anchorX, startY);
                canvas.Restore();
            }
        }

        // LogError
        // Logs an error that occurred during fade poster generation.
        protected override void LogError(Exception ex, string? episodeName)
        {
            _logger.LogError(ex, "Failed to generate fade poster for {EpisodeName}", episodeName);
        }
    }
}
