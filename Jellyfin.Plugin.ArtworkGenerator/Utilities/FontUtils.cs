using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;

namespace Jellyfin.Plugin.ArtworkGenerator.Utilities;

public static class FontUtils
{
    // TypefaceCache
    // Thread-safe cache for loaded typefaces to avoid repeated font loading.
    private static readonly ConcurrentDictionary<string, SKTypeface> TypefaceCache = new();

    // Families already reported as unavailable, so the warning is logged once rather than
    // once per episode.
    private static readonly ConcurrentDictionary<string, byte> ReportedMissingFamilies = new(StringComparer.OrdinalIgnoreCase);

    private static Action<string>? _missingFamilyReporter;

    /// <summary>
    /// Registers a callback invoked the first time a configured font family cannot be resolved
    /// on this system. Container images ship almost none of the common desktop fonts, and the
    /// resulting fallback to Skia's default is otherwise invisible to the user.
    /// </summary>
    public static void SetMissingFamilyReporter(Action<string>? reporter)
    {
        _missingFamilyReporter = reporter;
    }

    // GetCacheKey
    // Creates a unique cache key from font family and style parameters.
    private static string GetCacheKey(string? fontFamily, SKFontStyle style)
    {
        return $"{fontFamily ?? "default"}_{style.Weight}_{style.Width}_{style.Slant}";
    }

    // GetAvailableFontFamilies
    // Returns the font families installed on this system, sorted for display.
    public static IReadOnlyList<string> GetAvailableFontFamilies()
    {
        return SKFontManager.Default.FontFamilies
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    // CreateTypeface
    // Creates or retrieves a cached typeface from a font family name and style.
    //
    // MatchFamily does not report a miss: asked for a family the system does not have, it
    // quietly returns a substitute (on a container image, almost every desktop font name maps
    // to the one bundled face). The substitution is therefore detected by comparing the
    // resolved family name against the requested one, and reported once per family so the
    // fallback is visible in the server log instead of silently changing every poster.
    public static SKTypeface CreateTypeface(string fontFamilyName, SKFontStyle style)
    {
        var cacheKey = GetCacheKey(fontFamilyName, style);

        var typeface = TypefaceCache.GetOrAdd(
            cacheKey,
            _ => SKFontManager.Default.MatchFamily(fontFamilyName, style) ?? SKTypeface.Default);

        if (!string.IsNullOrWhiteSpace(fontFamilyName)
            && !string.Equals(typeface.FamilyName, fontFamilyName, StringComparison.OrdinalIgnoreCase)
            && ReportedMissingFamilies.TryAdd(fontFamilyName, 0))
        {
            _missingFamilyReporter?.Invoke(fontFamilyName);
        }

        return typeface;
    }

    // CreateTypefaceFromFile
    // Loads a typeface from a font file path, caching successful results.
    // Returns null if the file doesn't exist or loading fails. Failures are not cached
    // so the font will be retried if the file appears later (e.g. after a volume mount).
    public static SKTypeface? CreateTypefaceFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        var cacheKey = $"file:{filePath}";

        // Check cache first
        if (TypefaceCache.TryGetValue(cacheKey, out var cached))
            return cached;

        // Don't cache failures — only cache successful loads
        if (!File.Exists(filePath))
            return null;

        var typeface = SKTypeface.FromFile(filePath);
        if (typeface == null)
        {
            return null;
        }

        if (!TypefaceCache.TryAdd(cacheKey, typeface))
        {
            // Another thread won the race; drop this duplicate rather than leaking its handle.
            typeface.Dispose();
            return TypefaceCache.TryGetValue(cacheKey, out var winner) ? winner : null;
        }

        return typeface;
    }

    // ResolveTypeface
    // Tries to load a typeface from file path first, then falls back to font family name,
    // and finally to Skia's default face. Never returns null, so callers can render text
    // without null-checking every paint they build.
    public static SKTypeface ResolveTypeface(string? fontPath, string fontFamily, SKFontStyle style)
    {
        if (!string.IsNullOrWhiteSpace(fontPath))
        {
            var fileTypeface = CreateTypefaceFromFile(fontPath);
            if (fileTypeface != null)
                return fileTypeface;
        }

        return CreateTypeface(fontFamily, style);
    }

    // ClearCache
    // Clears the typeface cache, disposing the cached typefaces so their native handles are
    // released immediately rather than left to finalization. The shared default face is
    // process-wide and is never disposed here.
    public static void ClearCache()
    {
        foreach (var key in TypefaceCache.Keys)
        {
            if (TypefaceCache.TryRemove(key, out var typeface)
                && !ReferenceEquals(typeface, SKTypeface.Default))
            {
                typeface.Dispose();
            }
        }

        ReportedMissingFamilies.Clear();
    }

    // MeasureTextDimensions
    // Measures the tight glyph bounding box of the specified text.
    public static SKRect MeasureTextDimensions(string text, SKTypeface typeface, float fontSize)
    {
        using var font = PaintFactory.CreateFont(typeface, fontSize);
        font.MeasureText(text, out SKRect bounds);
        return bounds;
    }

    // CalculateOptimalFontSize
    // Calculates the optimal font size to fit text within specified dimensions using binary search.
    public static float CalculateOptimalFontSize(string text, SKTypeface typeface, float maxWidth, float maxHeight, float minFontSize = 10f, float tolerance = 0.5f)
    {
        float maxFontSize = maxHeight;
        float optimalSize = minFontSize;
        float low = minFontSize;
        float high = maxFontSize;

        while (low <= high)
        {
            float mid = low + (high - low) / 2;

            if (mid <= 0) break;

            var bounds = MeasureTextDimensions(text, typeface, mid);

            if (bounds.Width <= maxWidth && bounds.Height <= maxHeight)
            {
                optimalSize = mid;
                low = mid + tolerance;
            }
            else
            {
                high = mid - tolerance;
            }
        }

        return optimalSize;
    }

    // CalculateFontSizeFromPercentage
    // Converts a percentage-based font size to pixels based on poster height.
    public static int CalculateFontSizeFromPercentage(float percentage, float posterHeight, float posterMargin = 0)
    {
        if (percentage <= 0f || posterHeight <= 0f)
            return 0;

        return (int)(posterHeight * (percentage / (100f - (posterMargin * 2))));
    }

    // FontStyles
    // The style strings GetFontStyle understands, in the order the configuration page offers them.
    // Declared here so the list and the parser below cannot drift apart.
    public static IReadOnlyList<string> FontStyles { get; } = new[] { "Normal", "Bold", "Italic", "Bold Italic" };

    // GetFontStyle
    // Converts a font style string to an SKFontStyle enumeration.
    public static SKFontStyle GetFontStyle(string fontStyle)
    {
        return fontStyle.ToLowerInvariant() switch
        {
            "bold" => SKFontStyle.Bold,
            "italic" => SKFontStyle.Italic,
            "bold italic" => SKFontStyle.BoldItalic,
            _ => SKFontStyle.Normal,
        };
    }
}
