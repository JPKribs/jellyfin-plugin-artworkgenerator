using System;
using System.Collections.Generic;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Utilities;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Services.Posters
{
    public class FrostedGlassPosterGenerator : BasePosterGenerator
    {
        // Panel geometry as shares of the size unit.
        private const float PaddingXRatio = 0.07f;
        private const float PaddingYRatio = 0.03f;
        private const float CornerRadiusRatio = 0.02f;
        private const float BlurRatio = 0.018f;

        // Style
        // The poster style this generator produces.
        public override PosterStyle Style => PosterStyle.FrostedGlass;

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        public override string Description => "Text on a frosted glass panel that blurs the image behind it.";

        private readonly ILogger<FrostedGlassPosterGenerator> _logger;

        // The base canvas bitmap, captured during the canvas layer so the typography layer
        // can re-draw a blurred copy of it inside the panel. A generator renders one poster at a
        // time, so holding the reference between layers is safe.
        private SKBitmap? _canvasBitmap;

        // FrostedGlassPosterGenerator
        // Initializes a new instance of the frosted glass poster generator with logging support.
        public FrostedGlassPosterGenerator(ILogger<FrostedGlassPosterGenerator> logger)
        {
            _logger = logger;
        }

        // RenderCanvas
        // Draws the base canvas and captures it for the blurred panel rendering.
        protected override void RenderCanvas(SKCanvas skCanvas, SKBitmap canvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            _canvasBitmap = canvas;
            base.RenderCanvas(skCanvas, canvas, subject, settings, width, height);
        }

        // RenderTypography
        // Renders the frosted panel with the code and title centred inside it. The text carries
        // no drop shadow: the panel supplies the contrast.
        protected override void RenderTypography(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(skCanvas);
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            if (!settings.ShowPrimary && !settings.ShowSecondary)
            {
                return;
            }

            var unit = SizeUnit(width, height);
            var safeArea = GetSafeAreaBounds(width, height, settings);

            using var primaryStyle = CreatePrimaryStyle(settings, unit, SKTextAlign.Center, withShadow: false);
            using var secondaryStyle = CreateSecondaryStyle(settings, unit, SKTextAlign.Center, withShadow: false);

            float padX = unit * PaddingXRatio;
            float padY = unit * PaddingYRatio;
            float maxTextWidth = safeArea.Width - (2 * padX);

            var primaryLines = new List<string>();
            if (ShowsPrimary(settings, subject))
            {
                primaryLines.AddRange(TextUtils.FitTextLines(subject.Primary!, primaryStyle.Font, maxTextWidth, settings.LongTextHandling));
            }

            string? episodeText = ShowsSecondary(settings, subject) ? subject.SecondaryShort : null;

            if (primaryLines.Count == 0 && episodeText == null)
            {
                return;
            }

            float spacing = GetElementSpacing(settings, unit);

            // The panel is sized from the same column that positions its contents, so the box can
            // never be measured from one set of numbers and filled from another.
            var content = new LayoutColumn(SKRect.Create(safeArea.Left, 0, safeArea.Width, 0), spacing, LayoutAnchor.Top)
                .Add(SecondaryBlock, episodeText != null ? secondaryStyle.LineBox : 0f)
                .Add(PrimaryBlock, primaryLines.Count > 0 ? primaryStyle.BlockHeight(primaryLines.Count) : 0f);

            float contentHeight = content.Consumed;

            float contentWidth = 0;
            if (episodeText != null)
            {
                contentWidth = Math.Max(contentWidth, secondaryStyle.MeasureWidth(episodeText));
            }

            foreach (var line in primaryLines)
            {
                contentWidth = Math.Max(contentWidth, primaryStyle.MeasureWidth(line));
            }

            float panelWidth = Math.Min(safeArea.Width, contentWidth + (2 * padX));
            float panelHeight = contentHeight + (2 * padY);
            float panelLeft = safeArea.MidX - (panelWidth / 2f);
            float panelTop = safeArea.Bottom - panelHeight;
            var panelRect = new SKRect(panelLeft, panelTop, panelLeft + panelWidth, panelTop + panelHeight);
            using var roundedPanel = new SKRoundRect(panelRect, unit * CornerRadiusRatio);

            DrawFrostedPanel(skCanvas, roundedPanel, subject, settings, width, height);

            var placed = new LayoutColumn(
                SKRect.Create(panelRect.Left, panelTop + padY, panelRect.Width, contentHeight),
                spacing,
                LayoutAnchor.Top)
                .Add(SecondaryBlock, episodeText != null ? secondaryStyle.LineBox : 0f)
                .Add(PrimaryBlock, primaryLines.Count > 0 ? primaryStyle.BlockHeight(primaryLines.Count) : 0f);

            if (episodeText != null && placed.TryGetSlot(SecondaryBlock, out var secondarySlot))
            {
                secondaryStyle.Draw(skCanvas, episodeText, panelRect.MidX, secondaryStyle.BaselineAtTop(secondarySlot));
            }

            if (placed.TryGetSlot(PrimaryBlock, out var primarySlot))
            {
                primaryStyle.DrawLines(skCanvas, primaryLines, panelRect.MidX, primaryStyle.BaselineAtTop(primarySlot));
            }
        }

        // DrawFrostedPanel
        // Fills the rounded panel with a blurred copy of the canvas (matching the poster's
        // overlay tint) plus a frost wash, then strokes a subtle border.
        private void DrawFrostedPanel(SKCanvas skCanvas, SKRoundRect panel, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            var unit = SizeUnit(width, height);

            skCanvas.Save();
            skCanvas.ClipRoundRect(panel, antialias: true);

            if (_canvasBitmap != null)
            {
                float blurSigma = Math.Max(8f, unit * BlurRatio);

                // SKPaint does not own its image filter, so it is disposed explicitly rather
                // than left for finalization on every poster rendered.
                using var blurFilter = SKImageFilter.CreateBlur(blurSigma, blurSigma);
                using var blurPaint = new SKPaint
                {
                    IsAntialias = true,
                    ImageFilter = blurFilter
                };
                skCanvas.DrawBitmap(_canvasBitmap, 0, 0, blurPaint);

                // Re-apply the overlay inside the clip so the blurred panel keeps the same
                // tint as the rest of the poster instead of showing the raw frame colors.
                RenderOverlay(skCanvas, subject, settings, width, height);
            }

            using var frostPaint = PaintFactory.CreateFillPaint(SKColors.White.WithAlpha(38));
            skCanvas.DrawRect(panel.Rect, frostPaint);

            skCanvas.Restore();

            using var borderPaint = new SKPaint
            {
                Color = SKColors.White.WithAlpha(70),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1.5f, unit * 0.0015f),
                IsAntialias = true
            };
            skCanvas.DrawRoundRect(panel, borderPaint);
        }

        // LogError
        // Logs an error that occurred during frosted glass poster generation.
        protected override void LogError(Exception ex, string? episodeName)
        {
            _logger.LogError(ex, "Failed to generate frosted glass poster for {EpisodeName}", episodeName);
        }
    }
}
