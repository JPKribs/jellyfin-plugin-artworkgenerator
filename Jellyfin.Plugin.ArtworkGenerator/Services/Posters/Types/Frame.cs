using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using SkiaSharp;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Posters
{
    public class FramePosterGenerator : BasePosterGenerator
    {
        // Style
        // The poster style this generator produces.
        public override PosterStyle Style => PosterStyle.Frame;

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        public override string Description => "Image inside a decorative border. Polished gallery look.";

        // PrimaryDescription
        // One sentence on what the title is and where this style puts it.
        public override string PrimaryDescription
            => "The title is the item's own name, set on whichever border edge the text edges setting gives it.";

        // SecondaryDescription
        // One sentence on what the subtitle is and where this style puts it.
        public override string SecondaryDescription
            => "The subtitle is the episode code, set on the border edge left to it by the title.";

        // The frame is drawn around the title, so the title is always there, and this style is the
        // one that decides which edge holds it.
        public override IReadOnlyDictionary<string, PosterSettingState> SettingRules => PosterSettingRules.Build(
            (PosterSettingRules.ShowPrimary, PosterSettingState.Required),
            (PosterSettingRules.TextEdge, PosterSettingState.Optional),
            // Text position is hidden here: the two border edges are this design's own placement control.
            (PosterSettingRules.TextPosition, PosterSettingState.Hidden),
            (PosterSettingRules.TextAlignment, PosterSettingState.Hidden));

        // Border geometry at the 1080 pixel reference; scaled to the poster being drawn.
        private const float BorderStrokeReference = 4f;
        private const float BorderShadowStrokeReference = 6f;
        private const float CornerRadiusReference = 20f;

        // Gap between the frame line and the text set into it, as a share of the size unit.
        private const float TextPaddingRatio = 0.01f;

        // FramePosterGenerator
        // Initializes a new instance of the frame poster generator with logging support.
        public FramePosterGenerator(ILogger<FramePosterGenerator> logger)
            : base(logger)
        {
        }

        // RenderTypography
        // Renders the title and the subtitle set into the frame's edges, and the border itself,
        // which closes over any edge with no text in it. The title takes the edge the design asks
        // for and the subtitle takes the other.
        protected override void RenderTypography(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var unit = SizeUnit(width, height);
            var safeArea = GetSafeAreaBounds(width, height, settings);
            float spacing = GetElementSpacing(settings, unit);

            var showPrimary = ShowsPrimary(settings, subject);
            var showSecondary = ShowsSecondary(settings, subject);

            var (primaryAtBottom, secondaryAtBottom) = ResolveEdges(settings.TextEdge, showPrimary);

            TextInfo? topInfo = null;
            TextInfo? bottomInfo = null;

            if (showPrimary)
            {
                var info = DrawPrimaryLine(skCanvas, subject.Primary!, settings, unit, safeArea, primaryAtBottom);
                if (primaryAtBottom)
                {
                    bottomInfo = info;
                }
                else
                {
                    topInfo = info;
                }
            }

            if (showSecondary)
            {
                var info = DrawSecondaryLine(skCanvas, subject.Secondary, settings, unit, safeArea, secondaryAtBottom);
                if (secondaryAtBottom)
                {
                    bottomInfo = info;
                }
                else
                {
                    topInfo = info;
                }
            }

            DrawFrameBorder(skCanvas, safeArea, topInfo, bottomInfo, spacing, unit);
        }


        // ResolveEdges
        // Which edge each line takes. The "first" choices fill the chosen edge with whichever line
        // the item has, so a poster carrying a single line always looks the same whether that line
        // is a title or a subtitle. The pinned choices keep the title on its edge and leave the
        // other one empty when its line is missing.
        internal static (bool PrimaryAtBottom, bool SecondaryAtBottom) ResolveEdges(TextEdge edge, bool showPrimary)
        {
            var fillsBottom = edge is TextEdge.BottomFirst or TextEdge.AlwaysBottom;
            var pinned = edge is TextEdge.AlwaysTop or TextEdge.AlwaysBottom;

            return (fillsBottom, pinned || showPrimary ? !fillsBottom : fillsBottom);
        }

        // DrawPrimaryLine
        // Draws the uppercase title into the top edge of the safe area, or into the bottom edge
        // when nothing else claims it, and returns its extent. Null when the long title handling
        // drops a title that does not fit.
        private static TextInfo? DrawPrimaryLine(SKCanvas canvas, string title, PosterSettings config, int unit, SKRect safeArea, bool atBottom)
        {
            using var style = CreatePrimaryStyle(config, unit);

            var lines = TextUtils.FitTextLines(title.ToUpperInvariant(), style.Font, safeArea.Width * RenderConstants.TextWidthMultiplier, config.LongTextHandling);
            if (lines.Count == 0)
            {
                return null;
            }

            float blockHeight = style.BlockHeight(lines.Count);
            float padding = unit * TextPaddingRatio;
            float top = atBottom
                ? safeArea.Bottom - padding - blockHeight
                : safeArea.Top + padding;

            style.DrawLines(canvas, lines, safeArea.MidX, top + style.Ascent);

            return new TextInfo
            {
                Height = blockHeight,
                Width = lines.Max(line => style.MeasureWidth(line)),
                CenterX = safeArea.MidX,
                Y = top
            };
        }

        // DrawSecondaryLine
        // Draws the subtitle into whichever edge the title did not take, and returns its extent.
        private static TextInfo DrawSecondaryLine(SKCanvas canvas, string label, PosterSettings config, int unit, SKRect safeArea, bool atBottom)
        {
            using var style = CreateSecondaryStyle(config, unit);
            var padding = unit * TextPaddingRatio;

            if (!atBottom)
            {
                var top = safeArea.Top + padding;
                style.Draw(canvas, label, safeArea.MidX, top + style.Ascent);

                return new TextInfo
                {
                    Height = style.LineBox,
                    Width = style.MeasureWidth(label),
                    CenterX = safeArea.MidX,
                    Y = top
                };
            }

            var bottom = safeArea.Bottom - padding;
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
        private static void DrawFrameBorder(SKCanvas canvas, SKRect safeArea, TextInfo? primaryInfo, TextInfo? secondaryInfo, float spacing, int unit)
        {
            var radius = RenderConstants.Scaled(CornerRadiusReference, unit);

            using var borderPaint = PaintFactory.CreateLinePaint(SKColors.White, RenderConstants.Scaled(BorderStrokeReference, unit), SKStrokeCap.Round);
            borderPaint.StrokeJoin = SKStrokeJoin.Round;

            using var shadowPaint = PaintFactory.CreateLinePaint(SKColors.Black.WithAlpha(200), RenderConstants.Scaled(BorderShadowStrokeReference, unit), SKStrokeCap.Round);
            shadowPaint.StrokeJoin = SKStrokeJoin.Round;

            using var path = BuildFramePath(safeArea, radius, GapFor(primaryInfo, spacing), GapFor(secondaryInfo, spacing));

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
