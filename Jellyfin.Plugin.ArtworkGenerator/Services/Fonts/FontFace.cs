using SkiaSharp;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Fonts;

/// <summary>
/// One loaded font file, with the family and style read from the file itself.
/// </summary>
/// <param name="Family">The family name the font file gives.</param>
/// <param name="Weight">The weight the font file gives, such as 400 for Regular and 700 for Bold.</param>
/// <param name="Italic">Whether the font file is italic or oblique.</param>
/// <param name="Source">Where the font comes from.</param>
/// <param name="FileName">The file name in the fonts folder, or the resource name for a bundled font.</param>
/// <param name="Typeface">The loaded face, shared by every render and never disposed while listed.</param>
public sealed record FontFace(string Family, int Weight, bool Italic, FontSource Source, string FileName, SKTypeface Typeface)
{
    /// <summary>
    /// The weight at or above which a face counts as bold.
    /// </summary>
    public const int BoldWeight = 600;

    /// <summary>
    /// Gets the style as the Settings tab shows it, Regular, Bold, Italic, or Bold Italic.
    /// </summary>
    public string Style => (Weight >= BoldWeight, Italic) switch
    {
        (true, true) => "Bold Italic",
        (true, false) => "Bold",
        (false, true) => "Italic",
        _ => "Regular"
    };

    /// <summary>
    /// Builds a face from a loaded typeface, reading its family and style.
    /// </summary>
    /// <param name="typeface">The loaded face.</param>
    /// <param name="source">Where it comes from.</param>
    /// <param name="fileName">Its file or resource name.</param>
    /// <returns>The face.</returns>
    public static FontFace From(SKTypeface typeface, FontSource source, string fileName)
    {
        System.ArgumentNullException.ThrowIfNull(typeface);

        return new FontFace(
            typeface.FamilyName,
            typeface.FontWeight,
            typeface.FontSlant != SKFontStyleSlant.Upright,
            source,
            fileName,
            typeface);
    }
}
