using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Fonts;

/// <summary>
/// Every font family a design can use, looked up in order: uploaded fonts, then bundled fonts, then fonts installed on the server.
/// </summary>
public sealed class FontCatalog
{
    private readonly UploadedFontStore? _uploaded;
    private readonly Func<IReadOnlyList<FontFace>> _bundled;
    private readonly Func<IEnumerable<string>> _system;

    /// <summary>
    /// Initializes a new instance of the <see cref="FontCatalog"/> class over the plugin's fonts folder, its bundled fonts, and the server's fonts.
    /// </summary>
    /// <param name="uploaded">The fonts folder, or <c>null</c> for none.</param>
    public FontCatalog(UploadedFontStore? uploaded)
        : this(uploaded, () => BundledFonts.Faces, () => SKFontManager.Default.FontFamilies)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FontCatalog"/> class with the bundled and server lists given, so a test can control them.
    /// </summary>
    /// <param name="uploaded">The fonts folder, or <c>null</c> for none.</param>
    /// <param name="bundled">Gives the bundled faces.</param>
    /// <param name="system">Gives the family names installed on the server.</param>
    public FontCatalog(UploadedFontStore? uploaded, Func<IReadOnlyList<FontFace>> bundled, Func<IEnumerable<string>> system)
    {
        ArgumentNullException.ThrowIfNull(bundled);
        ArgumentNullException.ThrowIfNull(system);

        _uploaded = uploaded;
        _bundled = bundled;
        _system = system;
    }

    /// <summary>
    /// Gets the fonts folder, or <c>null</c> when there is none.
    /// </summary>
    public UploadedFontStore? Uploaded => _uploaded;

    /// <summary>
    /// Lists every family once, uploaded first, then bundled, then the server's, each group sorted by name.
    /// A family is listed under the first source that has it.
    /// </summary>
    /// <returns>The families and where each comes from.</returns>
    public IReadOnlyList<FontFamilyEntry> Families()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<FontFamilyEntry>();

        void AddGroup(IEnumerable<string> names, FontSource source)
        {
            foreach (var name in names
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                if (seen.Add(name))
                {
                    result.Add(new FontFamilyEntry(name, source));
                }
            }
        }

        AddGroup(UploadedFaces().Select(face => face.Family), FontSource.Uploaded);
        AddGroup(_bundled().Select(face => face.Family), FontSource.Bundled);
        AddGroup(_system(), FontSource.System);
        return result;
    }

    /// <summary>
    /// Finds the uploaded or bundled face for a family and style. An uploaded family wins over a bundled one of the same name.
    /// </summary>
    /// <param name="family">The family name, matched without regard to case.</param>
    /// <param name="style">The style wanted.</param>
    /// <returns>The closest face of the family, or <c>null</c> when neither source has it, so the server's fonts are tried next.</returns>
    public FontFace? Resolve(string? family, SKFontStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (string.IsNullOrWhiteSpace(family))
        {
            return null;
        }

        return ClosestStyle(UploadedFaces().Where(face => SameFamily(face, family)), style)
            ?? ClosestStyle(_bundled().Where(face => SameFamily(face, family)), style);
    }

    /// <summary>
    /// Picks the face nearest a style. The slant must match first, then the weight is the nearest, so Bold Italic falls back to Italic, then Bold, then Regular.
    /// </summary>
    /// <param name="faces">The faces of one family.</param>
    /// <param name="style">The style wanted.</param>
    /// <returns>The nearest face, or <c>null</c> when there are none.</returns>
    public static FontFace? ClosestStyle(IEnumerable<FontFace> faces, SKFontStyle style)
    {
        ArgumentNullException.ThrowIfNull(faces);
        ArgumentNullException.ThrowIfNull(style);

        var italic = style.Slant != SKFontStyleSlant.Upright;
        return faces
            .OrderBy(face => face.Italic == italic ? 0 : 1)
            .ThenBy(face => Math.Abs(face.Weight - style.Weight))
            .ThenByDescending(face => face.Weight)
            .FirstOrDefault();
    }

    private static bool SameFamily(FontFace face, string family)
        => string.Equals(face.Family, family.Trim(), StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<FontFace> UploadedFaces()
        => _uploaded?.List() ?? Array.Empty<FontFace>();
}
