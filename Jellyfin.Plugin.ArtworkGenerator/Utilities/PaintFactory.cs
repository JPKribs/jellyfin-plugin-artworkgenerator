using System;
using SkiaSharp;

namespace Jellyfin.Plugin.ArtworkGenerator.Utilities
{
    // PaintFactory
    // Factory for the fonts, paints, and bitmap draws every poster style shares, so the
    // rendering settings (antialiasing, sampling, shadow weight) live in one place.
    public static class PaintFactory
    {
        // CreateFont
        // A font set up for rendering to an offscreen surface: antialiased with subpixel
        // positioning. LCD (subpixel colour) edging is deliberately not requested; a raster
        // surface has no pixel geometry, so Skia would silently fall back anyway.
        public static SKFont CreateFont(SKTypeface typeface, float size)
        {
            return new SKFont(typeface, size)
            {
                Subpixel = true,
                Edging = SKFontEdging.Antialias,
                Hinting = SKFontHinting.Normal
            };
        }

        // CreateTextStyle
        // Builds the font, fill paint, and (optionally) shadow paint for a text element. The
        // shadow offset and blur scale with the poster so the lift under text reads the same
        // at every resolution.
        public static TextStyle CreateTextStyle(
            SKColor color,
            float fontSize,
            SKTypeface typeface,
            float posterHeight,
            SKTextAlign align = SKTextAlign.Center,
            bool withShadow = true)
        {
            var font = CreateFont(typeface, fontSize);
            var fill = new SKPaint
            {
                Color = color,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            if (!withShadow)
            {
                return new TextStyle(font, fill, null, null, align, 0f);
            }

            var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, RenderConstants.ShadowBlurSigma(posterHeight));
            var shadow = new SKPaint
            {
                Color = RenderConstants.ShadowColor,
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                MaskFilter = blur
            };

            return new TextStyle(font, fill, shadow, blur, align, RenderConstants.ShadowOffset(posterHeight));
        }

        // CreateLinePaint
        // Creates a paint for drawing lines with standard stroke settings.
        public static SKPaint CreateLinePaint(SKColor color, float strokeWidth, SKStrokeCap cap = SKStrokeCap.Square)
        {
            return new SKPaint
            {
                Color = color,
                StrokeWidth = strokeWidth,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true,
                StrokeCap = cap
            };
        }

        // CreateShadowLinePaint
        // Creates a paint for drawing shadow lines.
        public static SKPaint CreateShadowLinePaint(float strokeWidth, SKStrokeCap cap = SKStrokeCap.Square)
        {
            return CreateLinePaint(RenderConstants.ShadowColor, strokeWidth, cap);
        }

        // CreateFillPaint
        // Creates a paint for solid color fills.
        public static SKPaint CreateFillPaint(SKColor color)
        {
            return new SKPaint
            {
                Color = color,
                Style = SKPaintStyle.Fill
            };
        }

        // DrawLineWithShadow
        // Draws a line with a shadow effect offset by the poster-scaled shadow distance.
        public static void DrawLineWithShadow(
            SKCanvas canvas,
            float startX,
            float startY,
            float endX,
            float endY,
            SKPaint linePaint,
            SKPaint shadowPaint,
            float shadowOffset)
        {
            ArgumentNullException.ThrowIfNull(canvas);

            canvas.DrawLine(startX + shadowOffset, startY + shadowOffset, endX + shadowOffset, endY + shadowOffset, shadowPaint);
            canvas.DrawLine(startX, startY, endX, endY, linePaint);
        }

        // DrawBitmap
        // Draws a bitmap scaled into a destination rectangle with high quality resampling.
        public static void DrawBitmap(SKCanvas canvas, SKBitmap bitmap, SKRect dest, SKPaint? paint = null)
        {
            ArgumentNullException.ThrowIfNull(bitmap);
            DrawBitmap(canvas, bitmap, SKRect.Create(bitmap.Width, bitmap.Height), dest, paint, RenderConstants.HighQualitySampling);
        }

        // DrawBitmap
        // Draws a region of a bitmap into a destination rectangle with the given resampling.
        //
        // SkiaSharp 3 only exposes sampling options on the image draws, so the bitmap is
        // wrapped as an image over its own pixels; nothing is copied, and the wrapper is
        // released before returning.
        public static void DrawBitmap(SKCanvas canvas, SKBitmap bitmap, SKRect source, SKRect dest, SKPaint? paint, SKSamplingOptions sampling)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentNullException.ThrowIfNull(bitmap);

            using var pixmap = bitmap.PeekPixels();
            using var image = pixmap != null ? SKImage.FromPixels(pixmap) : null;

            if (image == null)
            {
                canvas.DrawBitmap(bitmap, source, dest, paint);
                return;
            }

            canvas.DrawImage(image, source, dest, sampling, paint);
        }

        // DrawBitmapCover
        // Fills the destination with the bitmap, scaling it up and cropping the overflow
        // equally on both sides, so artwork of any aspect ratio fills the box undistorted.
        public static void DrawBitmapCover(SKCanvas canvas, SKBitmap bitmap, SKRect dest, SKPaint? paint = null)
        {
            ArgumentNullException.ThrowIfNull(bitmap);

            if (bitmap.Width <= 0 || bitmap.Height <= 0 || dest.Width <= 0 || dest.Height <= 0)
            {
                return;
            }

            var sourceAspect = (float)bitmap.Width / bitmap.Height;
            var destAspect = dest.Width / dest.Height;

            SKRect source;
            if (sourceAspect > destAspect)
            {
                // Wider than the box: crop the sides.
                var cropWidth = bitmap.Height * destAspect;
                var left = (bitmap.Width - cropWidth) / 2f;
                source = new SKRect(left, 0, left + cropWidth, bitmap.Height);
            }
            else
            {
                // Taller than the box: crop top and bottom.
                var cropHeight = bitmap.Width / destAspect;
                var top = (bitmap.Height - cropHeight) / 2f;
                source = new SKRect(0, top, bitmap.Width, top + cropHeight);
            }

            DrawBitmap(canvas, bitmap, source, dest, paint, RenderConstants.HighQualitySampling);
        }
    }
}
