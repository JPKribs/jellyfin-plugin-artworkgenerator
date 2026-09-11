using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Utilities;
using SkiaSharp;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Services.Posters
{
    public class FramePosterGenerator : BasePosterGenerator
    {
        // Style
        // The poster style this generator produces.
        public override PosterStyle Style => PosterStyle.Frame;

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        public override string Description => "Image inside a decorative border. Polished gallery look.";

        // The frame is drawn around the title, so the title is always there.
        public override IReadOnlyDictionary<string, PosterSettingState> SettingRules => PosterSettingRules.Build(
            (PosterSettingRules.ShowTitle, PosterSettingState.Required));

        // Border geometry at the 1080 pixel reference; scaled to the poster being drawn.
        private const float BorderStrokeReference = 4f;
        private const float BorderShadowStrokeReference = 6f;
        private const float CornerRadiusReference = 20f;

        // Gap between the frame line and the text set into it, as a share of the size unit.
        private const float TextPaddingRatio = 0.01f;

        private readonly ILogger<FramePosterGenerator> _logger;

        // FramePosterGenerator
        // Initializes a new instance of the frame poster generator with logging support.
        public FramePosterGenerator(ILogger<FramePosterGenerator> logger)
        {
            _logger = logger;
        }

        // RenderTypography
        // Renders the title set into the top edge of the frame, the label set into the bottom
        // edge, and the border itself, which closes over any edge with no text in it.
        protected override void RenderTypography(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var unit = SizeUnit(width, height);
            var safeArea = GetSafeAreaBounds(width, height, settings);
            float spacing = GetElementSpacing(settings, unit);

            TextInfo? titleInfo = null;
            if (settings.ShowTitle && !string.IsNullOrEmpty(subject.Title))
            {
                titleInfo = DrawEpisodeTitle(skCanvas, subject.Title, settings, unit, safeArea);
            }

            TextInfo? episodeInfo = null;
            if (settings.ShowEpisode && !string.IsNullOrEmpty(subject.Label))
            {
                episodeInfo = DrawEpisodeInfo(skCanvas, subject.Label, settings, unit, safeArea);
            }

            DrawFrameBorder(skCanvas, safeArea, titleInfo, episodeInfo, spacing, unit);
        }

        // LogError
        // Logs an error that occurred during frame poster generation.
        protected override void LogError(Exception ex, string? episodeName)
        {
            _logger.LogError(ex, "Failed to generate frame poster for {EpisodeName}", episodeName);
        }

        // DrawEpisodeTitle
        // Draws the uppercase title across the top of the safe area and returns its extent,
        // or null when the long title handling drops a title that does not fit.
        private static TextInfo? DrawEpisodeTitle(SKCanvas canvas, string title, PosterSettings config, int unit, SKRect safeArea)
        {
            using var style = CreateTitleStyle(config, unit);

            var lines = TextUtils.FitTitleLines(title.ToUpperInvariant(), style.Font, safeArea.Width * RenderConstants.TextWidthMultiplier, config.LongTitleHandling);
            if (lines.Count == 0)
            {
                return null;
            }

            var top = safeArea.Top + (unit * TextPaddingRatio);
            style.DrawLines(canvas, lines, safeArea.MidX, top + style.Ascent);

            return new TextInfo
            {
                Height = style.BlockHeight(lines.Count),
                Width = lines.Max(line => style.MeasureWidth(line)),
                CenterX = safeArea.MidX,
                Y = top
            };
        }

        // DrawEpisodeInfo
        // Draws the label across the bottom of the safe area and returns its extent.
        private static TextInfo DrawEpisodeInfo(SKCanvas canvas, string label, PosterSettings config, int unit, SKRect safeArea)
        {
            using var style = CreateEpisodeStyle(config, unit);

            var bottom = safeArea.Bottom - (unit * TextPaddingRatio);
            style.Draw(canvas, label, safeArea.MidX, bottom - style.Descent);

            return new TextInfo
            {
                Height = style.LineBox,
                Width = style.MeasureWidth(label),
                CenterX = safeArea.MidX,
                Y = bottom - style.LineBox
            };
        }

        // DrawFrameBorder
        // Draws the rounded border with the top and bottom edges opened around whatever text sits
        // in them. The stroke, its shadow, and the corner radius all scale with the poster.
        private static void DrawFrameBorder(SKCanvas canvas, SKRect safeArea, TextInfo? titleInfo, TextInfo? episodeInfo, float spacing, int unit)
        {
            var radius = RenderConstants.Scaled(CornerRadiusReference, unit);

            using var borderPaint = PaintFactory.CreateLinePaint(SKColors.White, RenderConstants.Scaled(BorderStrokeReference, unit), SKStrokeCap.Round);
            borderPaint.StrokeJoin = SKStrokeJoin.Round;

            using var shadowPaint = PaintFactory.CreateLinePaint(SKColors.Black.WithAlpha(200), RenderConstants.Scaled(BorderShadowStrokeReference, unit), SKStrokeCap.Round);
            shadowPaint.StrokeJoin = SKStrokeJoin.Round;

            using var path = BuildFramePath(safeArea, radius, GapFor(titleInfo, spacing), GapFor(episodeInfo, spacing));

            canvas.DrawPath(path, shadowPaint);
            canvas.DrawPath(path, borderPaint);
        }

        // GapFor
        // The horizontal span an edge leaves open for a piece of text, or null for a closed edge.
        private static (float Left, float Right)? GapFor(TextInfo? info, float spacing)
        {
            if (!info.HasValue)
            {
                return null;
            }

            var half = info.Value.Width / 2f;
            return (info.Value.CenterX - half - spacing, info.Value.CenterX + half + spacing);
        }

        // BuildFramePath
        // One continuous outline, clockwise from the top edge, so the corners flow into the
        // edges without seams. An edge with a gap is drawn as two segments either side of it;
        // a gap that reaches a corner simply leaves that segment out.
        private static SKPath BuildFramePath(SKRect r, float radius, (float Left, float Right)? topGap, (float Left, float Right)? bottomGap)
        {
            var path = new SKPath();
            var d = radius * 2f;

            // Top edge, left to right.
            var topStart = r.Left + radius;
            var topEnd = r.Right - radius;
            path.MoveTo(topStart, r.Top);
            if (topGap.HasValue)
            {
                var gapLeft = Math.Clamp(topGap.Value.Left, topStart, topEnd);
                var gapRight = Math.Clamp(topGap.Value.Right, topStart, topEnd);
                if (gapLeft > topStart)
                {
                    path.LineTo(gapLeft, r.Top);
                }

                path.MoveTo(gapRight, r.Top);
                if (topEnd > gapRight)
                {
                    path.LineTo(topEnd, r.Top);
                }
            }
            else
            {
                path.LineTo(topEnd, r.Top);
            }

            path.ArcTo(new SKRect(r.Right - d, r.Top, r.Right, r.Top + d), 270, 90, false);
            path.LineTo(r.Right, r.Bottom - radius);
            path.ArcTo(new SKRect(r.Right - d, r.Bottom - d, r.Right, r.Bottom), 0, 90, false);

            // Bottom edge, right to left.
            var bottomStart = r.Right - radius;
            var bottomEnd = r.Left + radius;
            if (bottomGap.HasValue)
            {
                var gapRight = Math.Clamp(bottomGap.Value.Right, bottomEnd, bottomStart);
                var gapLeft = Math.Clamp(bottomGap.Value.Left, bottomEnd, bottomStart);
                if (gapRight < bottomStart)
                {
                    path.LineTo(gapRight, r.Bottom);
                }

                path.MoveTo(gapLeft, r.Bottom);
                if (gapLeft > bottomEnd)
                {
                    path.LineTo(bottomEnd, r.Bottom);
                }
            }
            else
            {
                path.LineTo(bottomEnd, r.Bottom);
            }

            path.ArcTo(new SKRect(r.Left, r.Bottom - d, r.Left + d, r.Bottom), 90, 90, false);
            path.LineTo(r.Left, r.Top + radius);
            path.ArcTo(new SKRect(r.Left, r.Top, r.Left + d, r.Top + d), 180, 90, false);

            return path;
        }
    }
}
