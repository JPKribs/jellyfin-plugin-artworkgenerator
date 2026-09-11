using System;
using System.Globalization;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Utilities;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Services.Posters
{
    public class TimelinePosterGenerator : BasePosterGenerator
    {
        // Style
        // The poster style this generator produces.
        public override PosterStyle Style => PosterStyle.Timeline;

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        public override string Description => "Season progress bar with episode info and optional title. Clean and data driven.";

        private const string LabelsBlock = "labels";
        private const string BarBlock = "bar";

        private readonly ILogger<TimelinePosterGenerator> _logger;

        // TimelinePosterGenerator
        // Initializes a new instance of the timeline poster generator with logging support.
        public TimelinePosterGenerator(ILogger<TimelinePosterGenerator> logger)
        {
            _logger = logger;
        }

        // RenderTypography
        // Renders, bottom-up: the season progress bar, the episode code and position labels
        // above it, and the optional episode title above those.
        protected override void RenderTypography(SKCanvas skCanvas, EpisodeMetadata episodeMetadata, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(episodeMetadata);
            ArgumentNullException.ThrowIfNull(settings);

            var safeArea = GetSafeAreaBounds(width, height, settings);

            using var titleStyle = CreateTitleStyle(settings, height, SKTextAlign.Left);
            using var episodeStyle = CreateEpisodeStyle(settings, height, SKTextAlign.Left);

            var episodeNumber = episodeMetadata.EpisodeNumberStart ?? 0;
            // When the season size is unknown the bar renders full rather than guessing.
            var totalEpisodes = episodeMetadata.SeasonEpisodeCount ?? episodeNumber;

            // Measured top-to-bottom: title, then the position labels, then the progress bar
            // pinned to the bottom of the safe area.
            var column = new LayoutColumn(safeArea, GetElementSpacing(settings, height), LayoutAnchor.Bottom)
                .Add(TitleBlock, settings.ShowTitle && !string.IsNullOrEmpty(episodeMetadata.EpisodeName)
                    ? titleStyle.BlockHeight(2)
                    : 0f)
                .Add(LabelsBlock, settings.ShowEpisode ? episodeStyle.LineBox : 0f)
                .Add(BarBlock, MeasureProgressBar(height));

            DrawProgressBar(skCanvas, settings, column.Slot(BarBlock), height, episodeNumber, totalEpisodes);

            if (column.TryGetSlot(LabelsBlock, out var labelsSlot))
            {
                DrawEpisodeLabels(skCanvas, episodeMetadata, episodeStyle, settings, height, labelsSlot, episodeNumber, totalEpisodes);
            }

            if (column.TryGetSlot(TitleBlock, out var titleSlot))
            {
                DrawTitleInSlot(skCanvas, episodeMetadata.EpisodeName!, titleStyle, titleSlot, titleSlot.Left, titleSlot.Width * RenderConstants.TextWidthMultiplier, settings.LongTitleHandling);
            }
        }

        // BarHeight
        // Track thickness, scaled with the poster.
        private static float BarHeight(int height) => Math.Max(4f, height * 0.008f);

        // MeasureProgressBar
        // Vertical extent of the bar including the marker dot, which overhangs the track.
        private static float MeasureProgressBar(int height)
        {
            return BarHeight(height) * 3f;
        }

        // DrawProgressBar
        // Draws the season timeline: a rounded track across the safe width, filled to the
        // episode's position, with a marker dot at the fill point kept inside the safe area.
        private static void DrawProgressBar(SKCanvas canvas, PosterSettings config, SKRect slot, int height, int episodeNumber, int totalEpisodes)
        {
            float barHeight = BarHeight(height);
            float dotRadius = barHeight * 1.5f;
            float barY = slot.MidY;

            float progress = totalEpisodes > 0
                ? Math.Clamp((float)episodeNumber / totalEpisodes, 0f, 1f)
                : 1f;

            var barColor = ColorUtils.ParseHexColor(config.EpisodeFontColor);
            float fillEndX = slot.Left + (slot.Width * progress);
            float dotX = Math.Clamp(fillEndX, slot.Left + dotRadius, slot.Right - dotRadius);

            using var trackPaint = PaintFactory.CreateLinePaint(barColor.WithAlpha((byte)(barColor.Alpha * 0.3f)), barHeight, SKStrokeCap.Round);
            using var fillPaint = PaintFactory.CreateLinePaint(barColor, barHeight, SKStrokeCap.Round);

            using var shadowBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, RenderConstants.ShadowBlurSigma(height));
            using var shadowPaint = PaintFactory.CreateShadowLinePaint(barHeight, SKStrokeCap.Round);
            shadowPaint.MaskFilter = shadowBlur;

            float shadowOffset = RenderConstants.ShadowOffset(height);
            canvas.DrawLine(slot.Left + shadowOffset, barY + shadowOffset, slot.Right + shadowOffset, barY + shadowOffset, shadowPaint);
            canvas.DrawLine(slot.Left, barY, slot.Right, barY, trackPaint);
            canvas.DrawLine(slot.Left, barY, fillEndX, barY, fillPaint);

            using var dotPaint = new SKPaint
            {
                Color = barColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawCircle(dotX, barY, dotRadius, dotPaint);
        }

        // DrawEpisodeLabels
        // Draws the episode code left-aligned and the season position right-aligned
        // directly above the progress bar.
        private static void DrawEpisodeLabels(SKCanvas canvas, EpisodeMetadata episodeMetadata, TextStyle leftStyle, PosterSettings config, int height, SKRect slot, int episodeNumber, int totalEpisodes)
        {
            using var rightStyle = CreateEpisodeStyle(config, height, SKTextAlign.Right);

            var codeText = EpisodeCodeUtils.FormatEpisodeCode(episodeMetadata.SeasonNumber ?? 0, episodeNumber);
            var positionText = episodeMetadata.SeasonEpisodeCount.HasValue
                ? string.Format(CultureInfo.InvariantCulture, "{0} OF {1}", episodeNumber, totalEpisodes)
                : string.Format(CultureInfo.InvariantCulture, "EPISODE {0}", episodeNumber);

            float baselineY = leftStyle.BaselineAtBottom(slot);

            leftStyle.Draw(canvas, codeText, slot.Left, baselineY);
            rightStyle.Draw(canvas, positionText, slot.Right, baselineY);
        }

        // LogError
        // Logs an error that occurred during timeline poster generation.
        protected override void LogError(Exception ex, string? episodeName)
        {
            _logger.LogError(ex, "Failed to generate timeline poster for {EpisodeName}", episodeName);
        }
    }
}
