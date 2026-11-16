namespace Jellyfin.Plugin.ArtworkGenerator.Services.Fonts;

/// <summary>
/// Where a font family comes from, in the order a family name is looked up.
/// </summary>
public enum FontSource
{
    /// <summary>
    /// A font an admin uploaded to the plugin's fonts folder.
    /// </summary>
    Uploaded,

    /// <summary>
    /// A font that ships inside the plugin.
    /// </summary>
    Bundled,

    /// <summary>
    /// A font installed on the server.
    /// </summary>
    System
}
