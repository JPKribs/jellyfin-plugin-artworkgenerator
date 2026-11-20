using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SkiaSharp;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Fonts;

/// <summary>
/// The fonts that ship inside the plugin as embedded resources, loaded once on first use and kept for the life of the process.
/// </summary>
public static class BundledFonts
{
    private const string ResourcePrefix = "Jellyfin.Plugin.ArtworkGenerator.Assets.Fonts.";

    private static readonly Lazy<IReadOnlyList<FontFace>> LoadedFaces = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Gets every bundled face, ordered by family, then weight, then upright before italic.
    /// </summary>
    public static IReadOnlyList<FontFace> Faces => LoadedFaces.Value;

    /// <summary>
    /// Gets the names of the bundled families, sorted.
    /// </summary>
    public static IReadOnlyList<string> Families => Faces
        .Select(face => face.Family)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(family => family, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    // Load
    // Reads each embedded .ttf with SKTypeface.FromStream. A resource Skia cannot read is skipped,
    // and BundledFontsTests proves that none is.
    private static FontFace[] Load()
    {
        var assembly = typeof(BundledFonts).Assembly;
        var faces = new List<FontFace>();

        foreach (var name in assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
        {
            var stream = assembly.GetManifestResourceStream(name);
            if (stream == null)
            {
                continue;
            }

            // The typeface takes the stream and reads glyphs from it later, so it stays open.
            var typeface = SKTypeface.FromStream(stream);
            if (typeface == null)
            {
                stream.Dispose();
                continue;
            }

            faces.Add(FontFace.From(typeface, FontSource.Bundled, name[ResourcePrefix.Length..]));
        }

        return faces
            .OrderBy(face => face.Family, StringComparer.OrdinalIgnoreCase)
            .ThenBy(face => face.Weight)
            .ThenBy(face => face.Italic)
            .ToArray();
    }
}
