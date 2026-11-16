namespace Jellyfin.Plugin.ArtworkGenerator.Models;

/// <summary>
/// One font sent from the Settings tab to be stored in the fonts folder.
/// </summary>
public sealed class FontUploadRequest
{
    /// <summary>
    /// Gets or sets the name the file was picked with, such as <c>Bold Titles.ttf</c>.
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// Gets or sets the file's bytes as base64.
    /// </summary>
    public string? Data { get; set; }
}
