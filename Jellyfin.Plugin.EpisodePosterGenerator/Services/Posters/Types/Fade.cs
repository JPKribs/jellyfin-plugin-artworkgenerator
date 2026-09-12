using System;
using System.Collections.Generic;
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

        // A series has no number, so its name runs up the poster instead. These bound it: how much
        // of the width the rotated letters may be tall, and how much of the run they may take. A
        // short name would otherwise be sized until it filled the height, which turns two words
        // into a slab.
        private const float FocalTitleThicknessRatio = 0.26f;
        private const float FocalTitleRunRatio = 0.8f;

        // Probe size for measuring a focal title. Text metrics scale linearly, so one measurement
        // sizes it.
        private const float FocalProbeSize = 100f;

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
                ? CreateFocalTitleStyle(config, unit, text, availableRun * FocalTitleRunRatio, safeArea.Width * FocalTitleThicknessRatio)
                : CreateTitleStyle(config, unit, SKTextAlign.Left);

            if (availableRun <= style.Size)
            {
                return;
            }

            // A focal title is already sized to run the whole height on one row. Wrapping it would
            // stack rows across the poster instead of along it, which is how it ran off the edge.
            var lines = focal
                ? (IReadOnlyList<string>)new[] { text }
                : TextUtils.FitTitleLines(text, style.Font, availableRun, config.LongTitleHandling);
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

            // Sized from what the canvas actually draws: the advance width along the run, and the
            // line box across it. Sizing from the glyphs' ink instead let the name run past the
            // safe area, since ink is narrower than the advance the draw call uses.
            using var probe = PaintFactory.CreateFont(typeface, FocalProbeSize);
            var metrics = probe.Metrics;
            float advance = probe.MeasureText(text);
            float lineBox = -metrics.Ascent + metrics.Descent;

            float byRun = advance > 0f ? run / advance * FocalProbeSize : run;
            float byThickness = lineBox > 0f ? thickness / lineBox * FocalProbeSize : thickness;
            float fontSize = Math.Max(8f, Math.Min(byRun, byThickness));

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
