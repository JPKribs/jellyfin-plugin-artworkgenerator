using System;
using System.Globalization;
using SkiaSharp;
using Jellyfin.Plugin.EpisodePosterGenerator.Configuration;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Utilities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Services
{
    // CroppingService
    // Handles cropping and resizing of extracted episode frames for posters.
    public class CroppingService
    {
        private const float DefaultAspectRatio = 16f / 9f;
        private const float AspectRatioTolerance = 0.01f;
        private const int MinCropPercentage = 4; // Minimum 25% of original (divisor)

        // A frame that lost most of its height to letterbox removal carries far fewer pixels than a
        // clean one, so the poster built from it came out visibly smaller than its neighbours in the
        // image picker. Cropped posters are scaled up until their short side reaches this, which is
        // the short side of a 2:3 poster 1080 tall.
        private const int MinShortSide = 720;

        // Never magnify more than this. Past it the result is soft rather than detailed, and a poster
        // that is honestly small beats one blown up into mush.
        private const float MaxUpscale = 2.0f;

        private readonly ILogger<CroppingService> _logger;

        // CroppingService
        // Initializes a new instance with the logger dependency.
        public CroppingService(ILogger<CroppingService> logger)
        {
            _logger = logger;
        }

        // CropPoster
        // Crops a source bitmap by removing letterbox/pillarbox and applying poster fill settings.
        // Returns the source unchanged when nothing needed cropping. Takes no video metadata: it
        // used to write the cropped dimensions back into the caller's metadata, which meant a crop
        // could silently mutate state shared across episodes. Callers now read the dimensions off
        // the returned bitmap.
        public SKBitmap CropPoster(SKBitmap source, PosterSettings settings)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(settings);

            var result = source;
            var originalSize = $"{source.Width}x{source.Height}";

            // Branch: Apply letterbox detection if enabled
            if (settings.EnableLetterboxDetection)
            {
                var cropped = RemoveLetterboxAndPillarbox(result, settings);
                if (cropped != result)
                {
                    _logger.LogInformation("Letterbox/pillarbox removed: {Original} -> {New}",
                        originalSize, $"{cropped.Width}x{cropped.Height}");

                    var oldResult = result;
                    result = cropped;
                    if (oldResult != source) oldResult.Dispose();
                }
            }

            // Branch: Apply poster fill unless Original mode is selected
            if (settings.PosterFill != PosterFill.Original)
            {
                var posterRatio = ParseAspectRatio(settings.PosterDimensionRatio);
                var filled = ApplyPosterFill(result, posterRatio, settings.PosterFill);

                if (filled != result)
                {
                    _logger.LogInformation("Poster fill applied: {Fill} ratio {Ratio}",
                        settings.PosterFill, settings.PosterDimensionRatio);

                    if (result != source)
                    {
                        using (result) { }
                    }
                    result = filled;
                }
            }
            else
            {
                _logger.LogDebug("PosterFill.Original selected - using image as-is after letterbox removal");
            }

            // Branch: bring a heavily cropped poster back up to a usable size. Original is left
            // alone, since it asks for the frame exactly as it came.
            if (settings.PosterFill != PosterFill.Original)
            {
                var (targetWidth, targetHeight) = NormalizedSize(result.Width, result.Height);
                if (targetWidth != result.Width || targetHeight != result.Height)
                {
                    _logger.LogInformation("Poster scaled up: {Original} -> {New}",
                        $"{result.Width}x{result.Height}", $"{targetWidth}x{targetHeight}");

                    var scaled = ScaleTo(result, targetWidth, targetHeight);
                    if (result != source)
                    {
                        using (result) { }
                    }

                    result = scaled;
                }
            }

            return result;
        }

        // NormalizedSize
        // The size a cropped poster is brought up to: scaled about its short side until that reaches
        // MinShortSide, preserving the aspect ratio, and never magnified beyond MaxUpscale.
        internal static (int Width, int Height) NormalizedSize(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return (width, height);
            }

            var shortSide = Math.Min(width, height);
            if (shortSide >= MinShortSide)
            {
                return (width, height);
            }

            var scale = Math.Min((float)MinShortSide / shortSide, MaxUpscale);

            return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
        }

        // ScaleTo
        // Redraws the bitmap at the given size with the same high quality sampling the fill uses.
        private static SKBitmap ScaleTo(SKBitmap source, int width, int height)
        {
            var scaled = new SKBitmap(width, height, source.ColorType, source.AlphaType);
            using var canvas = new SKCanvas(scaled);

            PaintFactory.DrawBitmap(
                canvas,
                source,
                new SKRect(0, 0, source.Width, source.Height),
                new SKRect(0, 0, width, height),
                null,
                RenderConstants.HighQualitySampling);

            return scaled;
        }

        // ParseAspectRatio
        // Parses an aspect ratio string like "16:9" or "2.35:1" into a float value.
        // The ratio is user-entered free text, so it is parsed with the invariant culture:
        // under a comma-decimal locale, current-culture parsing would read "2.35" as 235.
        internal static float ParseAspectRatio(string ratio)
        {
            if (string.IsNullOrWhiteSpace(ratio))
                return DefaultAspectRatio;

            var parts = ratio.Split(':');
            if (parts.Length == 2 &&
                float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float w) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float h) &&
                w > 0 && h > 0)
            {
                return w / h;
            }

            return DefaultAspectRatio;
        }

        // RemoveLetterboxAndPillarbox
        // Detects and removes black bars from the edges of an image using pixel analysis.
        private SKBitmap RemoveLetterboxAndPillarbox(SKBitmap source, PosterSettings settings)
        {
            var blackThreshold = settings.LetterboxBlackThreshold;
            var confidence = settings.LetterboxConfidence / 100.0f;

            _logger.LogDebug("Letterbox detection: threshold={Threshold}, confidence={Confidence:F3}",
                blackThreshold, confidence);

            // Detect black bars on all four edges by scanning rows/columns for black pixel ratio
            var top = DetectEdgeBlackBar(source, blackThreshold, confidence, isHorizontal: true, scanReverse: false);
            var bottom = DetectEdgeBlackBar(source, blackThreshold, confidence, isHorizontal: true, scanReverse: true);
            var left = DetectEdgeBlackBar(source, blackThreshold, confidence, isHorizontal: false, scanReverse: false);
            var right = DetectEdgeBlackBar(source, blackThreshold, confidence, isHorizontal: false, scanReverse: true);

            var newWidth = source.Width - left - right;
            var newHeight = source.Height - top - bottom;

            _logger.LogDebug("Detected bounds: left={Left}, top={Top}, right={Right}, bottom={Bottom}",
                left, top, right, bottom);
            _logger.LogDebug("Original: {OriginalWidth}x{OriginalHeight}, Detected: {NewWidth}x{NewHeight}",
                source.Width, source.Height, newWidth, newHeight);

            // Skip cropping if no significant bars detected or invalid dimensions
            if (newWidth <= 0 || newHeight <= 0 ||
                newWidth > source.Width || newHeight > source.Height ||
                (left == 0 && right == 0 && top == 0 && bottom == 0))
            {
                _logger.LogDebug("No significant letterbox/pillarbox detected");
                return source;
            }

            // Safety check: prevent overly aggressive cropping (minimum 25% of original)
            var minWidth = source.Width / MinCropPercentage;
            var minHeight = source.Height / MinCropPercentage;

            if (newWidth < minWidth || newHeight < minHeight)
            {
                _logger.LogWarning("Detected crop too aggressive, skipping: {NewWidth}x{NewHeight} vs minimum {MinWidth}x{MinHeight}",
                    newWidth, newHeight, minWidth, minHeight);
                return source;
            }

            var cropped = new SKBitmap(newWidth, newHeight, source.ColorType, source.AlphaType);
            using var canvas = new SKCanvas(cropped);

            var sourceRect = new SKRect(left, top, left + newWidth, top + newHeight);
            var destRect = new SKRect(0, 0, newWidth, newHeight);

            PaintFactory.DrawBitmap(canvas, source, sourceRect, destRect, null, RenderConstants.HighQualitySampling);

            return cropped;
        }

        // DetectEdgeBlackBar
        // Generic edge scanner that detects black bars from any edge.
        // For horizontal bars (top/bottom): scans rows, checks pixel ratio across width.
        // For vertical bars (left/right): scans columns, checks pixel ratio across height.
        // When scanReverse is true, scans from the far edge inward.
        //
        // Reads straight from the pixel buffer: a GetPixel call per pixel is a native
        // transition each, and four half-frame scans of a 4K frame add up to millions of them.
        private static int DetectEdgeBlackBar(
            SKBitmap bitmap,
            int blackThreshold,
            float confidence,
            bool isHorizontal,
            bool scanReverse)
        {
            var width = bitmap.Width;
            var height = bitmap.Height;
            var outerMax = isHorizontal ? height / 2 : width / 2;
            var outerTotal = isHorizontal ? height : width;
            var innerMax = isHorizontal ? width : height;

            // Both 8888 layouts keep the three colour channels in the first three bytes of each
            // pixel (in either order), and the brightness is their mean, so the order is moot.
            var fastPath = bitmap.ColorType is SKColorType.Rgba8888 or SKColorType.Bgra8888;
            var pixels = fastPath ? bitmap.GetPixelSpan() : ReadOnlySpan<byte>.Empty;
            var rowBytes = bitmap.RowBytes;

            for (int outer = 0; outer < outerMax; outer++)
            {
                int actualOuter = scanReverse ? (outerTotal - 1 - outer) : outer;
                int blackPixels = 0;

                for (int inner = 0; inner < innerMax; inner++)
                {
                    int x = isHorizontal ? inner : actualOuter;
                    int y = isHorizontal ? actualOuter : inner;

                    int brightness = fastPath
                        ? Brightness8888(pixels, (y * rowBytes) + (x * 4))
                        : Brightness(bitmap.GetPixel(x, y));

                    if (brightness <= blackThreshold)
                        blackPixels++;
                }

                float blackRatio = (float)blackPixels / innerMax;
                if (blackRatio < confidence)
                    return outer;
            }

            // Every scanned row/column was black. Report the full scanned depth rather than
            // zero ("no bar"); the caller's minimum-size guard rejects the degenerate result.
            return outerMax;
        }

        // Brightness8888
        // Mean of the three colour channels of a 4 byte pixel at the given byte offset.
        private static int Brightness8888(ReadOnlySpan<byte> pixels, int offset)
        {
            return (pixels[offset] + pixels[offset + 1] + pixels[offset + 2]) / 3;
        }

        // Brightness
        // Mean of the three colour channels of a pixel.
        private static int Brightness(SKColor pixel)
        {
            return (pixel.Red + pixel.Green + pixel.Blue) / 3;
        }

        // ApplyPosterFill
        // Adjusts the image dimensions to match the target aspect ratio using the specified fill mode.
        private SKBitmap ApplyPosterFill(SKBitmap source, float targetRatio, PosterFill fillMode)
        {
            var currentRatio = (float)source.Width / source.Height;

            if (Math.Abs(currentRatio - targetRatio) < AspectRatioTolerance)
                return source;

            int targetWidth, targetHeight;

            switch (fillMode)
            {
                // Branch: Fit mode crops to target ratio (zoom effect)
                case PosterFill.Fit:
                    if (currentRatio > targetRatio)
                    {
                        // Source wider than target - crop width
                        targetHeight = source.Height;
                        targetWidth = (int)(targetHeight * targetRatio);
                    }
                    else
                    {
                        // Source taller than target - crop height
                        targetWidth = source.Width;
                        targetHeight = (int)(targetWidth / targetRatio);
                    }
                    break;

                // Branch: Fill mode stretches to target ratio (may distort)
                case PosterFill.Fill:
                    var posterDimensions = CalculateTargetDimensions(source.Width, source.Height, targetRatio);
                    targetWidth = posterDimensions.width;
                    targetHeight = posterDimensions.height;
                    break;

                case PosterFill.Original:
                    return source;

                default:
                    return source;
            }

            if (targetWidth <= 0 || targetHeight <= 0)
                return source;

            var result = new SKBitmap(targetWidth, targetHeight, source.ColorType, source.AlphaType);
            using var canvas = new SKCanvas(result);

            SKRect sourceRect;
            var destRect = new SKRect(0, 0, targetWidth, targetHeight);

            if (fillMode == PosterFill.Fit)
            {
                var srcX = Math.Max(0, (source.Width - targetWidth) / 2);
                var srcY = Math.Max(0, (source.Height - targetHeight) / 2);
                sourceRect = new SKRect(srcX, srcY, srcX + targetWidth, srcY + targetHeight);
            }
            else
            {
                sourceRect = new SKRect(0, 0, source.Width, source.Height);
            }

            PaintFactory.DrawBitmap(canvas, source, sourceRect, destRect, null, RenderConstants.HighQualitySampling);

            return result;
        }

        // CalculateTargetDimensions
        // Calculates target width and height based on source dimensions and target aspect ratio.
        private (int width, int height) CalculateTargetDimensions(int sourceWidth, int sourceHeight, float targetRatio)
        {
            if (sourceWidth > sourceHeight)
            {
                int width = sourceWidth;
                int height = (int)(width / targetRatio);
                return (width, height);
            }
            else
            {
                int height = sourceHeight;
                int width = (int)(height * targetRatio);
                return (width, height);
            }
        }
    }
}
