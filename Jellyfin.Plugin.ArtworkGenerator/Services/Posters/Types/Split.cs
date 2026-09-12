using System;
using System.IO;
using SkiaSharp;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Posters
{
    public class SplitPosterGenerator : BasePosterGenerator
    {
        // Style
        // The poster style this generator produces.
        public override PosterStyle Style => PosterStyle.Split;

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        public override string Description => "Series poster beside the frame with text. Magazine layout.";

        // SupportedShapes
        // The layout puts a portrait series poster beside a frame, which needs a wide canvas.
        public override ArtworkShapes SupportedShapes => ArtworkShapes.Landscape;

        private readonly ILogger<SplitPosterGenerator> _logger;

        // SplitPosterGenerator
        // Initializes a new instance of the split poster generator with logging support.
        public SplitPosterGenerator(ILogger<SplitPosterGenerator> logger)
        {
            _logger = logger;
        }

        // RenderCanvas
        // Draws the series poster on the left and the extracted frame on the right. The poster
        // is centre-cropped to the 2:3 panel rather than stretched, so artwork that is not
        // exactly 2:3 keeps its proportions.
        protected override void RenderCanvas(SKCanvas skCanvas, SKBitmap canvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(skCanvas);
            ArgumentNullException.ThrowIfNull(subject);

            var seriesPosterPath = subject.VideoMetadata.SeriesPosterFilePath;
            var posterWidth = CalculatePosterWidth(height);

            // Draw extracted frame as base layer (full canvas)
            skCanvas.DrawBitmap(canvas, 0, 0);

            if (string.IsNullOrEmpty(seriesPosterPath) || !File.Exists(seriesPosterPath))
            {
                _logger.LogDebug("No series poster available, using fallback");
                DrawFallbackPoster(skCanvas, posterWidth, height);
                return;
            }

            try
            {
                using var posterStream = File.OpenRead(seriesPosterPath);
                using var seriesPoster = SKBitmap.Decode(posterStream);

                if (seriesPoster == null)
                {
                    _logger.LogWarning("Failed to decode series poster: {Path}", seriesPosterPath);
                    DrawFallbackPoster(skCanvas, posterWidth, height);
                    return;
                }

                using var posterPaint = new SKPaint { IsAntialias = true };
                PaintFactory.DrawBitmapCover(skCanvas, seriesPoster, new SKRect(0, 0, posterWidth, height), posterPaint);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load series poster: {Path}", seriesPosterPath);
                DrawFallbackPoster(skCanvas, posterWidth, height);
            }
        }

        // DrawFallbackPoster
        // Draws a solid dark rectangle when no series poster is available.
        private static void DrawFallbackPoster(SKCanvas skCanvas, int posterWidth, int height)
        {
            using var fallbackPaint = PaintFactory.CreateFillPaint(new SKColor(20, 20, 20));
            skCanvas.DrawRect(0, 0, posterWidth, height, fallbackPaint);
        }

        // RenderOverlay
        // Applies the overlay only to the right side (text area).
        protected override void RenderOverlay(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(skCanvas);
            ArgumentNullException.ThrowIfNull(settings);

            if (string.IsNullOrEmpty(settings.OverlayColor))
                return;

            var primaryColor = ColorUtils.ParseHexColor(settings.OverlayColor);
            if (primaryColor.Alpha == 0)
                return;

            var posterWidth = CalculatePosterWidth(height);
            var rightRect = SKRect.Create(posterWidth, 0, width - posterWidth, height);

            if (settings.OverlayGradient == OverlayGradient.None)
            {
                using var overlayPaint = PaintFactory.CreateFillPaint(primaryColor);
                skCanvas.DrawRect(rightRect, overlayPaint);
                return;
            }

            var secondaryColor = ColorUtils.ParseHexColor(settings.OverlaySecondaryColor);
            if (secondaryColor.Alpha == 0) secondaryColor = primaryColor;

            using var gradient = CreateOverlayGradient(settings.OverlayGradient, rightRect, primaryColor, secondaryColor);
            if (gradient == null)
                return;

            using var gradientPaint = new SKPaint
            {
                Shader = gradient,
                Style = SKPaintStyle.Fill,
                IsDither = true
            };
            skCanvas.DrawRect(rightRect, gradientPaint);
        }

        // RenderTypography
        // Renders the Standard text stack inside the right side safe area.
        protected override void RenderTypography(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var safeArea = GetRightSideSafeArea(width, height, settings);

            DrawBottomTextStack(skCanvas, safeArea, subject, settings, SizeUnit(width, height));
        }

        // LogError
        // Logs an error that occurred during split poster generation.
        protected override void LogError(Exception ex, string? episodeName)
        {
            _logger.LogError(ex, "Failed to generate split poster for {EpisodeName}", episodeName);
        }

        // CalculatePosterWidth
        // Calculates the width for a 2:3 aspect ratio poster that fills the full height.
        private static int CalculatePosterWidth(int height)
        {
            return (int)(height * (2.0 / 3.0));
        }

        // GetRightSideSafeArea
        // Calculates the safe area for the right side where text is rendered.
        private static SKRect GetRightSideSafeArea(int width, int height, PosterSettings settings)
        {
            var posterWidth = CalculatePosterWidth(height);
            var rightSideWidth = width - posterWidth;

            // Same pixel margin on all sides, derived from the poster height.
            var margin = SizeUnit(width, height) * GetSafeAreaMargin(settings);

            var safeLeft = posterWidth + margin;
            var safeTop = margin;
            var safeWidth = rightSideWidth - (2 * margin);
            var safeHeight = height - (2 * margin);

            return new SKRect(safeLeft, safeTop, safeLeft + safeWidth, safeTop + safeHeight);
        }
    }
}
