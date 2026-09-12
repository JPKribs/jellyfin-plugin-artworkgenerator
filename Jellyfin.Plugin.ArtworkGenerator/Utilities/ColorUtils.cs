using SkiaSharp;
using System;
using System.Globalization;

namespace Jellyfin.Plugin.ArtworkGenerator.Utilities;

public static class ColorUtils
{
    // ParseHexColor
    // Parses a hex color string and returns an SKColor.
    public static SKColor ParseHexColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return SKColors.White;

        hex = hex.TrimStart('#');
        var span = hex.AsSpan();
        var style = NumberStyles.HexNumber;
        var culture = CultureInfo.InvariantCulture;

        return span.Length switch
        {
            8 => new SKColor(
                byte.Parse(span.Slice(2, 2), style, culture),
                byte.Parse(span.Slice(4, 2), style, culture),
                byte.Parse(span.Slice(6, 2), style, culture),
                byte.Parse(span.Slice(0, 2), style, culture)
            ),

            6 => new SKColor(
                byte.Parse(span.Slice(0, 2), style, culture),
                byte.Parse(span.Slice(2, 2), style, culture),
                byte.Parse(span.Slice(4, 2), style, culture)
            ),

            _ => SKColors.White
        };
    }

    // ToArgbHex
    // Formats an SKColor as the #AARRGGBB hex notation used throughout the plugin settings.
    public static string ToArgbHex(SKColor color)
    {
        return $"#{color.Alpha:X2}{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
    }

    // GetContrastingOutline
    // Picks an outline color that reads against the given overlay: black over a light overlay,
    // white over a dark one, and a mid gray in the band where neither would stand out.
    // Shared by the Cutout text border and the Brush stroke outline so the two agree.
    public static SKColor GetContrastingOutline(SKColor overlayColor)
    {
        float r = overlayColor.Red / 255f;
        float g = overlayColor.Green / 255f;
        float b = overlayColor.Blue / 255f;

        r = r <= 0.03928f ? r / 12.92f : (float)Math.Pow((r + 0.055f) / 1.055f, 2.4f);
        g = g <= 0.03928f ? g / 12.92f : (float)Math.Pow((g + 0.055f) / 1.055f, 2.4f);
        b = b <= 0.03928f ? b / 12.92f : (float)Math.Pow((b + 0.055f) / 1.055f, 2.4f);

        float luminance = (0.2126f * r) + (0.7152f * g) + (0.0722f * b);
        return luminance > 0.5f ? SKColors.Black : luminance < 0.2f ? SKColors.White : new SKColor(64, 64, 64);
    }

    // Darken
    // Returns the color with its channels scaled toward black by the given factor (0-1).
    public static SKColor Darken(SKColor color, float factor)
    {
        factor = Math.Clamp(factor, 0f, 1f);
        return new SKColor(
            (byte)(color.Red * factor),
            (byte)(color.Green * factor),
            (byte)(color.Blue * factor),
            color.Alpha);
    }

    // EnsureMinimumBrightness
    // Raises a color's HSV value to at least the given floor (0-100), keeping its hue and
    // saturation, so a color sampled from dark artwork is still legible.
    public static SKColor EnsureMinimumBrightness(SKColor color, float minimumValue)
    {
        color.ToHsv(out float hue, out float saturation, out float value);
        if (value >= minimumValue)
            return color;

        return SKColor.FromHsv(hue, saturation, Math.Clamp(minimumValue, 0f, 100f), color.Alpha);
    }

    // GetDominantColor
    // Samples the bitmap for its dominant color: pixels are bucketed by hue and the densest
    // bucket of sufficiently saturated pixels is averaged. Falls back to the overall average
    // for low-saturation content, or SKColor.Empty when the bitmap is effectively transparent.
    public static SKColor GetDominantColor(SKBitmap bitmap)
    {
        if (bitmap == null || bitmap.Width == 0 || bitmap.Height == 0)
            return SKColor.Empty;

        // Downscale so the scan is cheap regardless of source resolution.
        const int SampleSize = 32;
        float scale = Math.Min((float)SampleSize / bitmap.Width, (float)SampleSize / bitmap.Height);
        int width = Math.Max(1, (int)(bitmap.Width * scale));
        int height = Math.Max(1, (int)(bitmap.Height * scale));

        using var sample = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(sample))
        {
            PaintFactory.DrawBitmap(canvas, bitmap, SKRect.Create(bitmap.Width, bitmap.Height), SKRect.Create(width, height), null, RenderConstants.FastSampling);
        }

        var pixels = sample.Pixels;
        if (pixels == null || pixels.Length == 0)
            return SKColor.Empty;

        const int HueBuckets = 12;
        var bucketCounts = new int[HueBuckets];
        var bucketR = new long[HueBuckets];
        var bucketG = new long[HueBuckets];
        var bucketB = new long[HueBuckets];
        long totalR = 0, totalG = 0, totalB = 0;
        int opaqueCount = 0;

        foreach (var pixel in pixels)
        {
            if (pixel.Alpha < 128)
                continue;

            opaqueCount++;
            totalR += pixel.Red;
            totalG += pixel.Green;
            totalB += pixel.Blue;

            pixel.ToHsv(out float hue, out float saturation, out float value);

            // Only saturated, mid-brightness pixels vote for a hue — this keeps near-black
            // shadows and blown-out highlights from dominating the palette.
            if (saturation < 15f || value < 10f || value > 95f)
                continue;

            int bucket = Math.Clamp((int)(hue / 360f * HueBuckets), 0, HueBuckets - 1);
            bucketCounts[bucket]++;
            bucketR[bucket] += pixel.Red;
            bucketG[bucket] += pixel.Green;
            bucketB[bucket] += pixel.Blue;
        }

        if (opaqueCount == 0)
            return SKColor.Empty;

        int best = 0;
        for (int i = 1; i < HueBuckets; i++)
        {
            if (bucketCounts[i] > bucketCounts[best])
                best = i;
        }

        // Require the winning hue to cover a meaningful share of the frame; otherwise the
        // content is effectively monochrome and the overall average represents it better.
        // The explicit > 0 guard matters for tiny samples where opaqueCount / 20 floors to 0.
        if (bucketCounts[best] > 0 && bucketCounts[best] * 20 >= opaqueCount)
        {
            var count = bucketCounts[best];
            return new SKColor(
                (byte)(bucketR[best] / count),
                (byte)(bucketG[best] / count),
                (byte)(bucketB[best] / count));
        }

        return new SKColor(
            (byte)(totalR / opaqueCount),
            (byte)(totalG / opaqueCount),
            (byte)(totalB / opaqueCount));
    }
}
