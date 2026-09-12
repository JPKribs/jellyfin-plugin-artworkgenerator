using System;
using System.Linq;
using System.Collections.Generic;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using SkiaSharp;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Posters
{
    public class CutoutPosterGenerator : BasePosterGenerator
    {
        // Style
        // The poster style this generator produces.
        public override PosterStyle Style => PosterStyle.Cutout;

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        public override string Description => "Large text cut out of the image. Bold and minimal.";

        // PrimaryDescription
        // One sentence on what the title is and where this style puts it.
        public override string PrimaryDescription
            => "The title is the item's own name, set below the cutout, and it becomes the cutout itself when the item has no number to cut.";

        // SecondaryDescription
        // One sentence on what the subtitle is and where this style puts it.
        public override string SecondaryDescription
            => "The subtitle is the episode number or code, and this style cuts it out of the image rather than drawing it as a line of text.";

        // The cutout letters are the poster, so the code or number is always drawn, and its size and
        // color come from the cutout itself rather than the text settings.
        public override IReadOnlyDictionary<string, PosterSettingState> SettingRules => PosterSettingRules.Build(
            (PosterSettingRules.CutoutType, PosterSettingState.Optional),
            (PosterSettingRules.CutoutBorder, PosterSettingState.Optional),
            (PosterSettingRules.ShowSecondary, PosterSettingState.Required),
            (PosterSettingRules.SecondaryFontSize, PosterSettingState.Hidden),
            (PosterSettingRules.SecondaryFontColor, PosterSettingState.Hidden),
            // Text position is hidden here: the lettering is cut out of the image and has to stay centered in it.
            (PosterSettingRules.TextPosition, PosterSettingState.Hidden));

        // Baseline-to-baseline spacing between stacked cutout words, relative to the font size.
        private const float WordLineSpacing = 1.1f;

        // The title never squeezes the cutout below this share of the safe height.
        // The cutout may shrink this far before the fit gives up. The old floor of 50 was taller
        // than a narrow portrait area allows, so the letters overflowed instead of shrinking.
        private const float MinimumCutoutFontSize = 12f;

        private const float MinimumCutoutAreaRatio = 0.6f;

        private readonly ILogger<CutoutPosterGenerator> _logger;
        private static readonly char[] WordSeparators = { ' ', '-' };

        // CutoutPosterGenerator
        // Initializes a new instance of the cutout poster generator with logging support.
        public CutoutPosterGenerator(ILogger<CutoutPosterGenerator> logger)
        {
            _logger = logger;
        }

        // RenderOverlay
        // Draws the overlay into its own layer and punches the code out of it, so the canvas shows
        // through the letters.
        protected override void RenderOverlay(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(skCanvas);
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            if (string.IsNullOrEmpty(settings.OverlayColor))
            {
                return;
            }

            var overlayColor = ColorUtils.ParseHexColor(settings.OverlayColor);
            if (overlayColor.Alpha == 0)
            {
                return;
            }

            skCanvas.SaveLayer();

            using (var overlayPaint = PaintFactory.CreateFillPaint(overlayColor))
            {
                skCanvas.DrawRect(SKRect.Create(width, height), overlayPaint);
            }

            DrawCutoutText(skCanvas, subject, settings, width, height, overlayColor);

            skCanvas.Restore();
        }

        // RenderTypography
        // Renders the optional title in the zone reserved beneath the cutout.
        protected override void RenderTypography(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            // A series' name is the cutout itself, so it is not drawn again beneath it.
            if (!settings.ShowPrimary || subject.CutoutIsPrimary || string.IsNullOrEmpty(subject.Primary))
            {
                return;
            }

            var unit = SizeUnit(width, height);
            var safeArea = GetSafeAreaBounds(width, height, settings);
            using var primaryStyle = CreatePrimaryStyle(settings, unit);

            var column = BuildColumn(safeArea, settings, unit, primaryStyle, true);
            if (column.TryGetSlot(PrimaryBlock, out var primarySlot))
            {
                DrawPrimaryInSlot(skCanvas, subject.Primary, primaryStyle, primarySlot, primarySlot.MidX, primarySlot.Width * RenderConstants.TextWidthMultiplier, settings.LongTextHandling);
            }
        }

        // LogError
        // Logs an error that occurred during cutout poster generation.
        protected override void LogError(Exception ex, string? episodeName)
        {
            _logger.LogError(ex, "Failed to generate cutout poster for {EpisodeName}", episodeName);
        }

        // BuildColumn
        // The one description of the vertical layout: a fixed two line title zone against the
        // bottom of the safe area. The cutout takes whatever the column leaves, so the title and
        // the letters are measured from the same numbers and cannot collide.
        private static LayoutColumn BuildColumn(SKRect safeArea, PosterSettings settings, int unit, TextStyle primaryStyle, bool reserveTitle)
        {
            return new LayoutColumn(safeArea, GetElementSpacing(settings, unit), LayoutAnchor.Bottom)
                .Add(PrimaryBlock, settings.ShowPrimary && reserveTitle ? primaryStyle.BlockHeight(2) : 0f);
        }

        // CalculateCutoutArea
        // The area left for the cutout text once the title zone is reserved.
        private static SKRect CalculateCutoutArea(SKRect safeArea, PosterSettings config, int unit, bool reserveTitle)
        {
            if (!config.ShowPrimary || !reserveTitle)
            {
                return safeArea;
            }

            using var primaryStyle = CreatePrimaryStyle(config, unit);
            var remaining = BuildColumn(safeArea, config, unit, primaryStyle, reserveTitle).Remaining;

            var minHeight = safeArea.Height * MinimumCutoutAreaRatio;
            return remaining.Height >= minHeight
                ? remaining
                : SKRect.Create(safeArea.Left, safeArea.Top, safeArea.Width, minHeight);
        }

        // DrawCutoutText
        // Draws the code as transparent cutouts in the overlay, with an optional outline.
        private static void DrawCutoutText(SKCanvas canvas, ArtworkSubject subject, PosterSettings config, int canvasWidth, int canvasHeight, SKColor overlayColor)
        {
            string cutoutText = subject.CutoutText(config.CutoutType);
            var words = cutoutText.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                return;
            }

            var safeArea = GetSafeAreaBounds(canvasWidth, canvasHeight, config);
            // Nothing is held back for a title the item does not have, such as a season named after
            // nothing but its number.
            var reserveTitle = !subject.CutoutIsPrimary && !string.IsNullOrWhiteSpace(subject.Primary);
            var cutoutArea = CalculateCutoutArea(safeArea, config, SizeUnit(canvasWidth, canvasHeight), reserveTitle);
            var typeface = ResolveSecondaryTypeface(config, FontUtils.GetFontStyle(config.SecondaryFontStyle));
            float fontSize = CalculateOptimalCutoutFontSize(words, typeface, cutoutArea);

            using var font = PaintFactory.CreateFont(typeface, fontSize);

            // The outline is drawn first, then the punch erases everything inside the glyphs,
            // leaving only the half of the stroke that lies outside the letter edge.
            if (config.CutoutBorder)
            {
                using var borderPaint = new SKPaint
                {
                    Color = ColorUtils.GetContrastingOutline(overlayColor),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = Math.Max(1f, fontSize * 0.015f),
                    IsAntialias = true,
                    StrokeCap = SKStrokeCap.Round,
                    StrokeJoin = SKStrokeJoin.Round
                };
                DrawCutoutTextCentered(canvas, words, font, borderPaint, cutoutArea);
            }

            using var cutoutPaint = new SKPaint
            {
                BlendMode = SKBlendMode.DstOut,
                IsAntialias = true
            };
            DrawCutoutTextCentered(canvas, words, font, cutoutPaint, cutoutArea);
        }

        // CalculateOptimalCutoutFontSize
        // Calculates the largest font size that fits all words within the available area.
        private static float CalculateOptimalCutoutFontSize(string[] words, SKTypeface typeface, SKRect availableArea)
        {
            float maxWidth = availableArea.Width;
            float maxHeight = availableArea.Height;

            if (words.Length == 1)
            {
                return ClampToWidth(words, typeface, FontUtils.CalculateOptimalFontSize(words[0], typeface, maxWidth, maxHeight, MinimumCutoutFontSize), maxWidth);
            }

            float maxFont = maxHeight / (words.Length * WordLineSpacing);
            float minFont = MinimumCutoutFontSize;
            float low = minFont;
            float high = maxFont;
            float optimal = minFont;

            while (high - low > 1f)
            {
                float test = (low + high) / 2f;
                if (DoAllWordsFit(words, typeface, test, maxWidth, maxHeight))
                {
                    optimal = test;
                    low = test;
                }
                else
                {
                    high = test;
                }
            }

            return ClampToWidth(words, typeface, optimal, maxWidth);
        }

        // ClampToWidth
        // The fit above measures the glyphs' ink, while the canvas draws them by their advance
        // width, which is wider. A tall, narrow poster turns that difference into letters that run
        // past the safe edge, so the size is scaled back by whatever the text really measures.
        private static float ClampToWidth(string[] words, SKTypeface typeface, float fontSize, float maxWidth)
        {
            using var font = PaintFactory.CreateFont(typeface, fontSize);
            float widest = words.Max(word => font.MeasureText(word));

            return widest > maxWidth && widest > 0f ? fontSize * (maxWidth / widest) : fontSize;
        }

        // DoAllWordsFit
        // Checks if all words fit within the specified dimensions at the given font size.
        private static bool DoAllWordsFit(string[] words, SKTypeface typeface, float fontSize, float maxWidth, float maxHeight)
        {
            float maxWordWidth = 0;
            foreach (var word in words)
            {
                var bounds = FontUtils.MeasureTextDimensions(word, typeface, fontSize);
                if (bounds.Width > maxWordWidth)
                {
                    maxWordWidth = bounds.Width;
                }
            }

            float totalHeight = words.Length * fontSize * WordLineSpacing;
            return maxWordWidth <= maxWidth && totalHeight <= maxHeight;
        }

        // DrawCutoutTextCentered
        // Draws the words centered horizontally and vertically in the area, stacked when there
        // is more than one.
        private static void DrawCutoutTextCentered(SKCanvas canvas, string[] words, SKFont font, SKPaint paint, SKRect area)
        {
            float centerX = area.MidX;
            float centerY = area.MidY;

            if (words.Length == 1)
            {
                // Center the glyphs' actual ink rather than the em box.
                font.MeasureText(words[0], out SKRect bounds);
                canvas.DrawText(words[0], centerX, centerY - bounds.MidY, SKTextAlign.Center, font, paint);
                return;
            }

            float lineHeight = font.Size * WordLineSpacing;
            float totalHeight = (words.Length * lineHeight) - (lineHeight - font.Size);
            float baseline = centerY - (totalHeight / 2f) + font.Size;

            foreach (var word in words)
            {
                canvas.DrawText(word, centerX, baseline, SKTextAlign.Center, font, paint);
                baseline += lineHeight;
            }
        }
    }
}
