using System;
using System.Collections.Generic;
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
        public override string Description => "Progress bar with the subtitle, the position, and an optional title. Clean and data driven.";

        // The bar and its labels are the style, so the code line is always drawn.
        public override IReadOnlyDictionary<string, PosterSettingState> SettingRules => PosterSettingRules.Build(
            (PosterSettingRules.ShowEpisode, PosterSettingState.Required));

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
        // Renders, bottom-up: the progress bar, the code and position labels above it, and the
        // optional title above those. An episode's bar runs through its season, a season's through
        // the series; a series has no position and draws a full bar.
        protected override void RenderTypography(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var unit = SizeUnit(width, height);
            var safeArea = GetSafeAreaBounds(width, height, settings);

            using var titleStyle = CreatePrimaryStyle(settings, unit, SKTextAlign.Left);
            using var episodeStyle = CreateSecondaryStyle(settings, unit, SKTextAlign.Left);

            // A series has no position to mark, so it gets the title alone rather than a bar that
            // would mean nothing.
            var hasProgress = subject.ProgressPosition.HasValue;
            var position = subject.ProgressPosition ?? 0;
            // When the length is unknown the bar renders full rather than guessing.
            var total = subject.ProgressTotal ?? position;

            var column = new LayoutColumn(safeArea, GetElementSpacing(settings, unit), LayoutAnchor.Bottom)
                .Add(PrimaryBlock, ShowsPrimary(settings, subject)
                    ? titleStyle.BlockHeight(2)
                    : 0f)
                .Add(LabelsBlock, settings.ShowEpisode && hasProgress ? episodeStyle.LineBox : 0f)
                .Add(BarBlock, hasProgress ? MeasureProgressBar(unit) : 0f);

            if (column.TryGetSlot(BarBlock, out var barSlot))
            {
                DrawProgressBar(skCanvas, settings, barSlot, unit, position, total);
            }

            if (column.TryGetSlot(LabelsBlock, out var labelsSlot))
            {
                DrawLabels(skCanvas, subject, episodeStyle, settings, unit, labelsSlot);
            }

            if (column.TryGetSlot(PrimaryBlock, out var titleSlot))
            {
                DrawTitleInSlot(skCanvas, subject.Primary!, titleStyle, titleSlot, titleSlot.Left, titleSlot.Width * RenderConstants.TextWidthMultiplier, settings.LongTitleHandling);
            }
        }

        // BarHeight
        // Track thickness, scaled with the poster.
        private static float BarHeight(int unit) => Math.Max(4f, unit * 0.008f);

        // MeasureProgressBar
        // Vertical extent of the bar including the marker dot, which overhangs the track.
        private static float MeasureProgressBar(int unit)
        {
            return BarHeight(unit) * 3f;
        }

        // DrawProgressBar
        // Draws the timeline: a rounded track across the safe width, filled to the position, with
        // a marker dot at the fill point kept inside the safe area.
        private static void DrawProgressBar(SKCanvas canvas, PosterSettings config, SKRect slot, int unit, int position, int total)
        {
            float barHeight = BarHeight(unit);
            float dotRadius = barHeight * 1.5f;
            float barY = slot.MidY;

            float progress = total > 0
                ? Math.Clamp((float)position / total, 0f, 1f)
                : 1f;

            var barColor = ColorUtils.ParseHexColor(config.EpisodeFontColor);
            float fillEndX = slot.Left + (slot.Width * progress);
            float dotX = Math.Clamp(fillEndX, slot.Left + dotRadius, slot.Right - dotRadius);

            using var trackPaint = PaintFactory.CreateLinePaint(barColor.WithAlpha((byte)(barColor.Alpha * 0.3f)), barHeight, SKStrokeCap.Round);
            using var fillPaint = PaintFactory.CreateLinePaint(barColor, barHeight, SKStrokeCap.Round);

            using var shadowBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, RenderConstants.ShadowBlurSigma(unit));
            using var shadowPaint = PaintFactory.CreateShadowLinePaint(barHeight, SKStrokeCap.Round);
            shadowPaint.MaskFilter = shadowBlur;

            float shadowOffset = RenderConstants.ShadowOffset(unit);
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

        // DrawLabels
        // Draws the code left-aligned and the position right-aligned directly above the bar.
        private static void DrawLabels(SKCanvas canvas, ArtworkSubject subject, TextStyle leftStyle, PosterSettings config, int unit, SKRect slot)
        {
            using var rightStyle = CreateSecondaryStyle(config, unit, SKTextAlign.Right);

            float baselineY = leftStyle.BaselineAtBottom(slot);

            leftStyle.Draw(canvas, subject.SecondaryShort, slot.Left, baselineY);
            rightStyle.Draw(canvas, subject.ProgressText, slot.Right, baselineY);
        }

        // LogError
        // Logs an error that occurred during timeline poster generation.
        protected override void LogError(Exception ex, string? episodeName)
        {
            _logger.LogError(ex, "Failed to generate timeline poster for {EpisodeName}", episodeName);
        }
    }
}
