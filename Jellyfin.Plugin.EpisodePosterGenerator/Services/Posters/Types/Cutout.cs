using System;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Utilities;
using SkiaSharp;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Services.Posters
{
    public class CutoutPosterGenerator : BasePosterGenerator
    {
        // Style
        // The poster style this generator produces.
        public override PosterStyle Style => PosterStyle.Cutout;

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        public override string Description => "Large episode code cut out of the image. Bold and minimal.";

        // Baseline-to-baseline spacing between stacked cutout words, relative to the font size.
        private const float WordLineSpacing = 1.1f;

        // The title never squeezes the cutout below this share of the safe height.
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
        // Draws the overlay into its own layer and punches the episode text out of it, so the
        // canvas shows through the letters. The layer replaces a full-size scratch bitmap that
        // used to be allocated and blitted for every poster.
        protected override void RenderOverlay(SKCanvas skCanvas, EpisodeMetadata episodeMetadata, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(skCanvas);
            ArgumentNullException.ThrowIfNull(settings);

            if (string.IsNullOrEmpty(settings.OverlayColor))
                return;

            var overlayColor = ColorUtils.ParseHexColor(settings.OverlayColor);
            if (overlayColor.Alpha == 0)
                return;

            skCanvas.SaveLayer();

            using (var overlayPaint = PaintFactory.CreateFillPaint(overlayColor))
            {
                skCanvas.DrawRect(SKRect.Create(width, height), overlayPaint);
            }

            DrawCutoutText(skCanvas, episodeMetadata, settings, width, height, overlayColor);

            skCanvas.Restore();
        }

        // RenderTypography
        // Renders the optional episode title in the zone reserved beneath the cutout.
        protected override void RenderTypography(SKCanvas skCanvas, EpisodeMetadata episodeMetadata, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(episodeMetadata);
            ArgumentNullException.ThrowIfNull(settings);

            if (!settings.ShowTitle || string.IsNullOrEmpty(episodeMetadata.EpisodeName))
                return;

            var safeArea = GetSafeAreaBounds(width, height, settings);
            using var titleStyle = CreateTitleStyle(settings, height);

            var column = BuildColumn(safeArea, settings, height, titleStyle);
            if (column.TryGetSlot(TitleBlock, out var titleSlot))
            {
                DrawTitleInSlot(skCanvas, episodeMetadata.EpisodeName, titleStyle, titleSlot, titleSlot.MidX, titleSlot.Width * RenderConstants.TextWidthMultiplier, settings.LongTitleHandling);
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
        private static LayoutColumn BuildColumn(SKRect safeArea, PosterSettings settings, int height, TextStyle titleStyle)
        {
            return new LayoutColumn(safeArea, GetElementSpacing(settings, height), LayoutAnchor.Bottom)
                .Add(TitleBlock, settings.ShowTitle ? titleStyle.BlockHeight(2) : 0f);
        }

        // CalculateCutoutArea
        // The area left for the cutout text once the title zone is reserved.
        private static SKRect CalculateCutoutArea(SKRect safeArea, PosterSettings config, int height)
        {
            if (!config.ShowTitle)
                return safeArea;

            using var titleStyle = CreateTitleStyle(config, height);
            var remaining = BuildColumn(safeArea, config, height, titleStyle).Remaining;

            var minHeight = safeArea.Height * MinimumCutoutAreaRatio;
            return remaining.Height >= minHeight
                ? remaining
                : SKRect.Create(safeArea.Left, safeArea.Top, safeArea.Width, minHeight);
        }

        // DrawCutoutText
        // Draws the episode code as transparent cutouts in the overlay, with an optional outline.
        private static void DrawCutoutText(SKCanvas canvas, EpisodeMetadata episodeMetadata, PosterSettings config, int canvasWidth, int canvasHeight, SKColor overlayColor)
        {
            var safeArea = GetSafeAreaBounds(canvasWidth, canvasHeight, config);

            string episodeText = EpisodeCodeUtils.FormatEpisodeText(config.CutoutType,
                episodeMetadata.SeasonNumber ?? 0,
                episodeMetadata.EpisodeNumberStart ?? 0);
            var words = episodeText.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);

            var cutoutArea = CalculateCutoutArea(safeArea, config, canvasHeight);
            var typeface = ResolveEpisodeTypeface(config, FontUtils.GetFontStyle(config.EpisodeFontStyle));
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
                return FontUtils.CalculateOptimalFontSize(words[0], typeface, maxWidth, maxHeight, 50f);

            float maxFont = maxHeight / (words.Length * WordLineSpacing);
            float minFont = 30f;
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

            return optimal;
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
                    maxWordWidth = bounds.Width;
            }

            float totalHeight = words.Length * fontSize * WordLineSpacing;
            return maxWordWidth <= maxWidth && totalHeight <= maxHeight;
        }

        // DrawCutoutTextCentered
        // Draws the words centred horizontally and vertically in the area, stacked when there
        // is more than one.
        private static void DrawCutoutTextCentered(SKCanvas canvas, string[] words, SKFont font, SKPaint paint, SKRect area)
        {
            float centerX = area.MidX;
            float centerY = area.MidY;

            if (words.Length == 1)
            {
                // Centre the glyphs' actual ink rather than the em box.
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
