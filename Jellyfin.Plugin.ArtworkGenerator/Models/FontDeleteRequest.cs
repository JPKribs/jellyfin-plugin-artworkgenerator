namespace Jellyfin.Plugin.ArtworkGenerator.Models;

/// <summary>
/// Names one font in the fonts folder to delete.
/// </summary>
public sealed class FontDeleteRequest
{
    /// <summary>
    /// Gets or sets the file name, as the list of uploaded fonts gives it.
    /// </summary>
    public string? FileName { get; set; }
}
