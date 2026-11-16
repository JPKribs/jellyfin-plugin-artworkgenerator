namespace Jellyfin.Plugin.ArtworkGenerator.Services.Fonts;

/// <summary>
/// The outcome of an upload, the stored face or a sentence saying why the font was refused.
/// </summary>
/// <param name="Face">The stored face, or <c>null</c> when refused.</param>
/// <param name="Error">The reason the font was refused, or <c>null</c> when stored.</param>
public sealed record FontUploadResult(FontFace? Face, string? Error)
{
    /// <summary>
    /// Builds the result for a stored font.
    /// </summary>
    /// <param name="face">The stored face.</param>
    /// <returns>The result.</returns>
    public static FontUploadResult Stored(FontFace face) => new(face, null);

    /// <summary>
    /// Builds the result for a refused font.
    /// </summary>
    /// <param name="error">One sentence saying why.</param>
    /// <returns>The result.</returns>
    public static FontUploadResult Refused(string error) => new(null, error);
}
