using System;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.ArtworkGenerator.Services
{
    /// <summary>
    /// Applies brightness adjustments to bitmaps, primarily for boosting
    /// HDR content that appears dim after tone mapping.
    /// </summary>
    public class BrightnessService
    {
        private readonly ILogger<BrightnessService> _logger;

        public BrightnessService(ILogger<BrightnessService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Increases the brightness of a bitmap in-place by the specified percentage.
        /// </summary>
        public void BrightenBitmap(SKBitmap bitmap, double brightnessIncrease)
        {
            if (bitmap == null) return;
            if (brightnessIncrease <= 0) return;

            try
            {
                _logger.LogDebug("Brightening bitmap by {Increase}%", brightnessIncrease);

                var multiplier = 1.0f + (float)(brightnessIncrease / 100.0);

                // Redraw through a scaling color filter instead of round-tripping
                // SKBitmap.Pixels, which would allocate and copy back the whole frame twice
                // (~66 MB for a single 4K canvas).
                using var filter = SKColorFilter.CreateColorMatrix(new[]
                {
                    multiplier, 0, 0, 0, 0,
                    0, multiplier, 0, 0, 0,
                    0, 0, multiplier, 0, 0,
                    0, 0, 0, 1, 0
                });

                using var paint = new SKPaint { ColorFilter = filter };
                using var snapshot = bitmap.Copy();
                using var canvas = new SKCanvas(bitmap);

                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(snapshot, 0, 0, paint);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to brighten bitmap");
            }
        }
    }
}
