using System;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Utilities;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Services.Posters
{
    public class StripedPosterGenerator : BasePosterGenerator
    {
        // Band geometry. The centre line is a share of the poster height, so the sash sits low on
        // both shapes; the thicknesses are shares of the size unit, so the band keeps its weight
        // on a tall portrait instead of growing with the height. The sash is drawn wider than the
        // canvas so its ends stay covered at the tilt angle.
        private const float BandAngleDegrees = -7f;
        private const float BandCenterYRatio = 0.74f;
        private const float BandHeightRatio = 0.14f;
        private const float PinstripeHeightRatio = 0.018f;
        private const float PinstripeGapRatio = 0.018f;

        // The band caps the text size so it never spills off the sash.
        private const float BandTextHeightRatio = 0.55f;

        // Style
        // The poster style this generator produces.
        public override PosterStyle Style => PosterStyle.Striped;

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        public override string Description => "Tilted pinstriped sash carrying the title. Sporty and graphic.";

        private readonly ILogger<StripedPosterGenerator> _logger;

        // StripedPosterGenerator
        // Initializes a new instance of the striped poster generator with logging support.
        public StripedPosterGenerator(ILogger<StripedPosterGenerator> logger)
        {
            _logger = logger;
        }

        // RenderOverlay
        // Draws the tilted sash: a solid main band with a thin pinstripe above and below,
        // using the overlay color for the band and the secondary color for the pinstripes.
        protected override void RenderOverlay(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(skCanvas);
            ArgumentNullException.ThrowIfNull(settings);

            if (string.IsNullOrEmpty(settings.OverlayColor))
            {
                return;
            }

            var bandColor = ColorUtils.ParseHexColor(settings.OverlayColor);
            if (bandColor.Alpha == 0)
            {
                return;
            }

            var pinstripeColor = ColorUtils.ParseHexColor(settings.OverlaySecondaryColor);
            if (pinstripeColor.Alpha == 0)
            {
                pinstripeColor = bandColor;
            }

            var unit = SizeUnit(width, height);
            float bandCenterY = height * BandCenterYRatio;
            float bandHeight = unit * BandHeightRatio;
            float pinHeight = unit * PinstripeHeightRatio;
            float pinGap = unit * PinstripeGapRatio;

            // Overdraw horizontally so the tilted band's ends never expose the corners.
            float overdraw = width * 0.25f;

            skCanvas.Save();
            skCanvas.RotateDegrees(BandAngleDegrees, width / 2f, bandCenterY);

            using var bandPaint = PaintFactory.CreateFillPaint(bandColor);
            using var pinPaint = PaintFactory.CreateFillPaint(pinstripeColor);

            float bandTop = bandCenterY - (bandHeight / 2f);
            skCanvas.DrawRect(new SKRect(-overdraw, bandTop, width + overdraw, bandTop + bandHeight), bandPaint);
            skCanvas.DrawRect(new SKRect(-overdraw, bandTop - pinGap - pinHeight, width + overdraw, bandTop - pinGap), pinPaint);
            skCanvas.DrawRect(new SKRect(-overdraw, bandTop + bandHeight + pinGap, width + overdraw, bandTop + bandHeight + pinGap + pinHeight), pinPaint);

            skCanvas.Restore();
        }

        // RenderTypography
        // Draws the title along the sash and the code in the top-right corner. When there is no
        // title the code rides the sash instead.
        protected override void RenderTypography(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var unit = SizeUnit(width, height);
            var safeArea = GetSafeAreaBounds(width, height, settings);
            var code = subject.Code;
            var showCode = settings.ShowEpisode && code.Length > 0;

            bool titleOnBand = false;
            if (settings.ShowTitle && !string.IsNullOrEmpty(subject.Title))
            {
                using var titleStyle = CreateBandStyle(settings, unit, true);
                titleOnBand = DrawBandText(skCanvas, subject.Title, titleStyle, settings, width, height, safeArea);
            }

            // When there is no title on the band (disabled, or dropped by the long
            // title handling), the code rides the band instead of the corner.
            if (!titleOnBand)
            {
                if (showCode)
                {
                    using var codeStyle = CreateBandStyle(settings, unit, false);
                    DrawBandText(skCanvas, code, codeStyle, settings, width, height, safeArea);
                }

                return;
            }

            if (showCode)
            {
                DrawCornerEpisodeCode(skCanvas, code, settings, unit, safeArea);
            }
        }

        // CreateBandStyle
        // The title or code style with its configured size capped by the band thickness.
        private static TextStyle CreateBandStyle(PosterSettings settings, int unit, bool title)
        {
            float bandCap = unit * BandHeightRatio * BandTextHeightRatio;

            if (title)
            {
                float configured = FontUtils.CalculateFontSizeFromPercentage(settings.TitleFontSize, unit);
                var typeface = FontUtils.ResolveTypeface(settings.EffectiveTitleFontPath, settings.TitleFontFamily, FontUtils.GetFontStyle(settings.TitleFontStyle));
                return PaintFactory.CreateTextStyle(ColorUtils.ParseHexColor(settings.TitleFontColor), Math.Min(configured, bandCap), typeface, unit);
            }

            float configuredCode = FontUtils.CalculateFontSizeFromPercentage(settings.EpisodeFontSize, unit);
            var codeTypeface = ResolveEpisodeTypeface(settings, FontUtils.GetFontStyle(settings.EpisodeFontStyle));
            return PaintFactory.CreateTextStyle(ColorUtils.ParseHexColor(settings.EpisodeFontColor), Math.Min(configuredCode, bandCap), codeTypeface, unit);
        }

        // DrawBandText
        // Draws a single line of text centred along the tilted sash, fitted to the safe width
        // using the long title handling. Returns false when the handling drops the text.
        private static bool DrawBandText(SKCanvas canvas, string text, TextStyle style, PosterSettings settings, int width, int height, SKRect safeArea)
        {
            float bandCenterY = height * BandCenterYRatio;
            float maxTextWidth = safeArea.Width * RenderConstants.TextWidthMultiplier;

            var line = TextUtils.FitTitleLine(text, style.Font, maxTextWidth, settings.LongTitleHandling);
            if (line == null)
            {
                return false;
            }

            // Centre the ascent-to-descent box on the band's centre line.
            float baselineY = bandCenterY + ((style.Ascent - style.Descent) / 2f);

            canvas.Save();
            canvas.RotateDegrees(BandAngleDegrees, width / 2f, bandCenterY);
            style.Draw(canvas, line, width / 2f, baselineY);
            canvas.Restore();
            return true;
        }

        // DrawCornerEpisodeCode
        // Draws the code horizontally in the top-right corner of the safe area, deliberately
        // unrotated to contrast with the tilted sash.
        private static void DrawCornerEpisodeCode(SKCanvas canvas, string code, PosterSettings settings, int unit, SKRect safeArea)
        {
            using var style = CreateEpisodeStyle(settings, unit, SKTextAlign.Right);
            style.Draw(canvas, code, safeArea.Right, style.BaselineAtTop(safeArea));
        }

        // LogError
        // Logs an error that occurred during striped poster generation.
        protected override void LogError(Exception ex, string? episodeName)
        {
            _logger.LogError(ex, "Failed to generate striped poster for {EpisodeName}", episodeName);
        }
    }
}
