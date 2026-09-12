using System.Collections.Generic;
using System;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Posters
{
    public class StripedPosterGenerator : BasePosterGenerator
    {
        // Band geometry. The center line is a share of the poster height, so the sash sits low on
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
        public override string Description => "Tilted pinstriped sash carrying the title, with the subtitle in the corner. Sporty and graphic.";

        // SettingText
        // The sash is drawn from the two colors directly, one for the band and one for its stripes, so
        // neither is optional and a wash direction does not apply.
        public override IReadOnlyDictionary<string, SettingText> SettingText { get; }
            = new Dictionary<string, SettingText>(StringComparer.Ordinal)
            {
                [PosterSettingRules.OverlayColor] = new(
                    "Band Color",
                    "The color of the sash."),
                [PosterSettingRules.OverlaySecondaryColor] = new(
                    "Pinstripe Color",
                    "The color of the stripes running along the sash.")
            };

        // SettingRules
        // Text position is hidden here: the title rides the tilted sash, which is the design.
        public override IReadOnlyDictionary<string, PosterSettingState> SettingRules => PosterSettingRules.Build(
            (PosterSettingRules.OverlayGradient, PosterSettingState.Hidden),
            (PosterSettingRules.OverlaySecondaryColor, PosterSettingState.Optional),
            (PosterSettingRules.TextPosition, PosterSettingState.Hidden),
            (PosterSettingRules.TextAlignment, PosterSettingState.Hidden));

        // PrimaryDescription
        // One sentence on what the title is and where this style puts it.
        public override string PrimaryDescription
            => "The title is the item's own name, set along the tilted sash.";

        // SecondaryDescription
        // One sentence on what the subtitle is and where this style puts it.
        public override string SecondaryDescription
            => "The subtitle is the episode code, set in the corner, or on the sash itself when there is no title.";

        // StripedPosterGenerator
        // Initializes a new instance of the striped poster generator with logging support.
        public StripedPosterGenerator(ILogger<StripedPosterGenerator> logger)
            : base(logger)
        {
        }

        // RenderOverlay
        // Draws the tilted sash: a solid main band with a thin pinstripe above and below,
        // using the overlay color for the band and the secondary color for the pinstripes.
        protected override void RenderOverlay(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(skCanvas);
            ArgumentNullException.ThrowIfNull(settings);

            if (!TryGetOverlayColor(settings, out var bandColor))
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
        // MeasureTypography
        // The title rides the sash, which is the design itself rather than a block laid over it.
        protected override SKRect MeasureTypography(ArtworkSubject subject, PosterSettings settings, int width, int height)
            => SKRect.Empty;

        protected override void RenderTypography(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var unit = SizeUnit(width, height);
            var safeArea = GetSafeAreaBounds(width, height, settings);
            var code = subject.SecondaryShort;
            var showCode = ShowsSecondary(settings, subject) && code.Length > 0;

            bool titleOnBand = false;
            if (ShowsPrimary(settings, subject))
            {
                using var primaryStyle = CreateBandStyle(settings, unit, true);
                titleOnBand = DrawBandText(skCanvas, subject.Primary!, primaryStyle, settings, width, height, safeArea);
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
                float configured = FontUtils.CalculateFontSizeFromPercentage(settings.PrimaryFontSize, unit);
                var typeface = FontUtils.ResolveTypeface(settings.EffectivePrimaryFontPath, settings.PrimaryFontFamily, FontUtils.GetFontStyle(settings.PrimaryFontStyle));
                return PaintFactory.CreateTextStyle(ColorUtils.ParseHexColor(settings.PrimaryFontColor), Math.Min(configured, bandCap), typeface, unit);
            }

            float configuredCode = FontUtils.CalculateFontSizeFromPercentage(settings.SecondaryFontSize, unit);
            var codeTypeface = ResolveSecondaryTypeface(settings);
            return PaintFactory.CreateTextStyle(ColorUtils.ParseHexColor(settings.SecondaryFontColor), Math.Min(configuredCode, bandCap), codeTypeface, unit);
        }

        // DrawBandText
        // Draws a single line of text centered along the tilted sash, fitted to the safe width
        // using the long title handling. Returns false when the handling drops the text.
        private static bool DrawBandText(SKCanvas canvas, string text, TextStyle style, PosterSettings settings, int width, int height, SKRect safeArea)
        {
            float bandCenterY = height * BandCenterYRatio;
            float maxTextWidth = safeArea.Width * RenderConstants.TextWidthMultiplier;

            var line = TextUtils.FitTitleLine(text, style.Font, maxTextWidth, settings.LongTextHandling);
            if (line == null)
            {
                return false;
            }

            // Center the ascent-to-descent box on the band's center line.
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
            using var style = CreateSecondaryStyle(settings, unit, SKTextAlign.Right);
            style.Draw(canvas, code, safeArea.Right, style.BaselineAtTop(safeArea));
        }

    }
}
