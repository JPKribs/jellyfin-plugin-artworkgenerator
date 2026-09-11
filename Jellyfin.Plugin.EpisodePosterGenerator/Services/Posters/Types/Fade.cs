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

        // A series has no number, so its name runs the full height instead. This is how much of
        // the width the rotated letters may be tall.
        private const float FocalTitleThicknessRatio = 0.35f;

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
                // The title is always sideways: that is the style. A series has no number to sit
                // above, so its name runs the whole height and is sized to fill it.
                DrawVerticalTitle(skCanvas, subject.Title, settings, unit, safeArea, numberTop, !subject.Number.HasValue);
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
        // Draws the uppercase title rotated 90 degrees, running upward along the left edge from
        // just above the number. The title wraps to up to two vertical rows before the long title
        // handling engages, matching the other styles. A focal title, drawn when there is no number
        // to share the poster with, is sized to fill the run rather than taking the set size.
        private static void DrawVerticalTitle(SKCanvas canvas, string title, PosterSettings config, int unit, SKRect safeArea, float numberTop, bool focal)
        {
            float spacing = GetElementSpacing(config, unit);
            float startY = numberTop - spacing;
            float availableRun = startY - safeArea.Top;
            if (availableRun <= 0f)
            {
                return;
            }

            var text = title.ToUpperInvariant();

            using var style = focal
                ? CreateFocalTitleStyle(config, unit, text, availableRun, safeArea.Width * FocalTitleThicknessRatio)
                : CreateTitleStyle(config, unit, SKTextAlign.Left);

            if (availableRun <= style.Size)
            {
                return;
            }

            var lines = TextUtils.FitTitleLines(text, style.Font, availableRun, config.LongTitleHandling);
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

        // CreateFocalTitleStyle
        // The title face sized to fill the vertical run: the name reads the length of the poster,
        // and its letters are as tall across the width as the thickness budget allows.
        private static TextStyle CreateFocalTitleStyle(PosterSettings config, int unit, string text, float run, float thickness)
        {
            var typeface = FontUtils.ResolveTypeface(config.EffectiveTitleFontPath, config.TitleFontFamily, FontUtils.GetFontStyle(config.TitleFontStyle));
            float fontSize = FontUtils.CalculateOptimalFontSize(text, typeface, run, thickness);

            return PaintFactory.CreateTextStyle(ColorUtils.ParseHexColor(config.TitleFontColor), fontSize, typeface, unit, SKTextAlign.Left);
        }

        // LogError
        // Logs an error that occurred during fade poster generation.
        protected override void LogError(Exception ex, string? episodeName)
        {
            _logger.LogError(ex, "Failed to generate fade poster for {EpisodeName}", episodeName);
        }
    }
}
