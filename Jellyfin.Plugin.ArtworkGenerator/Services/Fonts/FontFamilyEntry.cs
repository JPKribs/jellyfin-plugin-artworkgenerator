namespace Jellyfin.Plugin.ArtworkGenerator.Services.Fonts;

/// <summary>
/// One family in the font list the Designs and Logos tabs offer.
/// </summary>
/// <param name="Family">The family name.</param>
/// <param name="Source">The first source that has the family.</param>
public sealed record FontFamilyEntry(string Family, FontSource Source);
