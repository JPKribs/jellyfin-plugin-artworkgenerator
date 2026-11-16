using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Services.Fonts;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// A family name resolves to an uploaded font first, then a bundled one, then the server's, and the font list names each family once in that order.
/// </summary>
public sealed class FontCatalogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ag-catalog-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Deletes the temp folder.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    private FontCatalog Catalog(params string[] system)
        => new(new UploadedFontStore(_folder), () => BundledFonts.Faces, () => system);

    /// <summary>
    /// An uploaded font of a bundled family's name wins, so an admin can replace a bundled face.
    /// </summary>
    [Fact]
    public void AnUploadedFontWinsOverABundledOne()
    {
        var catalog = Catalog();
        catalog.Uploaded!.Upload("lora.ttf", UploadedFontStoreTests.FontBytes("Lora.Lora-Regular.ttf"));

        var face = catalog.Resolve("Lora", SKFontStyle.Normal);

        Assert.Equal(FontSource.Uploaded, face!.Source);
        Assert.Equal("lora.ttf", face.FileName);
    }

    /// <summary>
    /// With nothing uploaded, a bundled family resolves to its bundled face, matched without regard to case.
    /// </summary>
    [Fact]
    public void ABundledFamilyResolvesWhenNothingIsUploaded()
    {
        var face = Catalog().Resolve("playfair display", SKFontStyle.Bold);

        Assert.Equal(FontSource.Bundled, face!.Source);
        Assert.Equal("Playfair Display", face.Family);
        Assert.Equal("Bold", face.Style);
    }

    /// <summary>
    /// A family neither uploaded nor bundled is left to the server's fonts.
    /// </summary>
    [Theory]
    [InlineData("A Font Nobody Has 4242")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnknownFamilyIsLeftToTheServer(string? family)
    {
        Assert.Null(Catalog().Resolve(family, SKFontStyle.Normal));
    }

    /// <summary>
    /// Each style resolves to the face that ships it.
    /// </summary>
    [Theory]
    [InlineData("Normal", "Regular")]
    [InlineData("Bold", "Bold")]
    [InlineData("Italic", "Italic")]
    [InlineData("Bold Italic", "Bold Italic")]
    public void EachStyleResolvesToItsFace(string style, string expected)
    {
        var face = Catalog().Resolve("Montserrat", Utilities.FontUtils.GetFontStyle(style));

        Assert.Equal(expected, face!.Style);
    }

    /// <summary>
    /// A style a family lacks falls back sensibly: italic keeps the weight, and a missing italic uses the upright face of the nearest weight.
    /// </summary>
    [Theory]
    [InlineData("Oswald", "Italic", "Regular")]
    [InlineData("Oswald", "Bold Italic", "Bold")]
    [InlineData("Anton", "Bold", "Regular")]
    [InlineData("Bebas Neue", "Bold Italic", "Regular")]
    public void AMissingStyleFallsBackToTheNearest(string family, string style, string expected)
    {
        var face = Catalog().Resolve(family, Utilities.FontUtils.GetFontStyle(style));

        Assert.Equal(expected, face!.Style);
    }

    /// <summary>
    /// Bold Italic with only Bold and Italic to choose from keeps the slant, as browsers do.
    /// </summary>
    [Fact]
    public void BoldItalicPrefersItalicOverBold()
    {
        var faces = BundledFonts.Faces.Where(f => f.Family == "Lora" && (f.Style == "Bold" || f.Style == "Italic"));

        Assert.Equal("Italic", FontCatalog.ClosestStyle(faces, SKFontStyle.BoldItalic)!.Style);
    }

    /// <summary>
    /// The list names uploaded families first, then bundled, then the server's, each group sorted, and a family only once under its first source.
    /// </summary>
    [Fact]
    public void TheListIsOrderedBySourceWithNoDuplicates()
    {
        var catalog = Catalog("Zapfino", "Lora", "Arial", "arial", " ");
        catalog.Uploaded!.Upload("lora.ttf", UploadedFontStoreTests.FontBytes("Lora.Lora-Regular.ttf"));

        var families = catalog.Families();

        Assert.Equal(new FontFamilyEntry("Lora", FontSource.Uploaded), families[0]);
        Assert.Equal(
            BundledFonts.Families.Where(f => f != "Lora"),
            families.Where(f => f.Source == FontSource.Bundled).Select(f => f.Family));
        Assert.Equal(new[] { "Arial", "Zapfino" }, families.Where(f => f.Source == FontSource.System).Select(f => f.Family));
        Assert.Equal(families.Count, families.Select(f => f.Family).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// A deleted upload stops resolving, and the bundled face of that family takes over.
    /// </summary>
    [Fact]
    public void DeletingAnUploadFallsBackToTheBundledFace()
    {
        var catalog = Catalog();
        catalog.Uploaded!.Upload("lora.ttf", UploadedFontStoreTests.FontBytes("Lora.Lora-Regular.ttf"));

        catalog.Uploaded.Delete("lora.ttf");

        Assert.Equal(FontSource.Bundled, catalog.Resolve("Lora", SKFontStyle.Normal)!.Source);
    }
}
