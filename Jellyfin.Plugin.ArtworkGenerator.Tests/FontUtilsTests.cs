using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.ArtworkGenerator.Services.Fonts;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using SkiaSharp;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Tests for font resolution and sizing in <see cref="FontUtils"/>.
/// </summary>
public class FontUtilsTests
{
    /// <summary>
    /// Callers build paints directly from this, so it must never hand back null even when the
    /// configured family is absent — which is the normal case on a container image.
    /// </summary>
    [Theory]
    [InlineData("Arial")]
    [InlineData("A Font That Is Definitely Not Installed 12345")]
    [InlineData("")]
    public void CreateTypeface_AlwaysResolvesToAUsableFace(string family)
    {
        Assert.NotNull(FontUtils.CreateTypeface(family, SKFontStyle.Normal));
    }

    [Fact]
    public void ResolveTypeface_FallsBackWhenTheFontFileIsMissing()
    {
        var typeface = FontUtils.ResolveTypeface(
            "/nonexistent/path/to/font.ttf",
            "A Font That Is Definitely Not Installed 12345",
            SKFontStyle.Normal);

        Assert.NotNull(typeface);
    }

    /// <summary>
    /// SkiaSharp substitutes rather than failing — asked for a font it does not have, it quietly
    /// returns a different face. The substitution is therefore detected by comparing resolved
    /// family names, and must be reported once per family rather than once per poster.
    /// </summary>
    [Fact]
    public void SetMissingFamilyReporter_FiresOncePerUnknownFamily()
    {
        FontUtils.ClearCache();

        var reported = new List<string>();
        FontUtils.SetMissingFamilyReporter(reported.Add);

        try
        {
            const string missing = "Another Font Nobody Has 98765";
            FontUtils.CreateTypeface(missing, SKFontStyle.Normal);
            FontUtils.CreateTypeface(missing, SKFontStyle.Normal);

            Assert.Equal(new[] { missing }, reported);
        }
        finally
        {
            FontUtils.SetMissingFamilyReporter(null);
            FontUtils.ClearCache();
        }
    }

    /// <summary>
    /// A bundled family resolves to its own face and raises no missing font warning.
    /// </summary>
    [Fact]
    public void SetMissingFamilyReporter_StaysQuietForABundledFamily()
    {
        FontUtils.ClearCache();

        var reported = new List<string>();
        FontUtils.SetMissingFamilyReporter(reported.Add);

        try
        {
            var typeface = FontUtils.CreateTypeface("Oswald", SKFontStyle.Bold);

            Assert.Equal("Oswald", typeface.FamilyName);
            Assert.Equal(700, typeface.FontWeight);
            Assert.Empty(reported);
        }
        finally
        {
            FontUtils.SetMissingFamilyReporter(null);
            FontUtils.ClearCache();
        }
    }

    /// <summary>
    /// Text drawn through CreateTypeface uses an uploaded font first, then a bundled one, then the server's.
    /// </summary>
    [Fact]
    public void CreateTypeface_ResolvesUploadedThenBundledThenSystem()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ag-fontutils-" + Guid.NewGuid().ToString("N"));
        var original = FontUtils.Catalog;
        var store = new UploadedFontStore(folder);
        FontUtils.SetCatalog(new FontCatalog(store));

        try
        {
            Assert.Same(BundledFonts.Faces.First(f => f.Family == "Roboto" && f.Style == "Regular").Typeface, FontUtils.CreateTypeface("Roboto", SKFontStyle.Normal));

            var uploaded = store.Upload("roboto.ttf", UploadedFontStoreTests.FontBytes("Roboto.Roboto-Regular.ttf")).Face!;
            Assert.Same(uploaded.Typeface, FontUtils.CreateTypeface("Roboto", SKFontStyle.Normal));

            var system = SKFontManager.Default.FontFamilies.FirstOrDefault(f => !BundledFonts.Families.Contains(f, StringComparer.OrdinalIgnoreCase));
            if (system != null)
            {
                Assert.Equal(system, FontUtils.CreateTypeface(system, SKFontStyle.Normal).FamilyName, ignoreCase: true);
            }
        }
        finally
        {
            FontUtils.SetCatalog(original);
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// The font list names each family once, with the bundled families always in it.
    /// </summary>
    [Fact]
    public void GetFontFamilies_ListsEachFamilyOnceWithTheBundledOnes()
    {
        var families = FontUtils.GetFontFamilies();

        Assert.All(families, f => Assert.False(string.IsNullOrWhiteSpace(f.Family)));
        Assert.Equal(families.Count, families.Select(f => f.Family).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(BundledFonts.Families, b => Assert.Contains(families, f => f.Family == b));
    }

    [Theory]
    [InlineData(10f, 1000f, 0f, 100)]
    [InlineData(0f, 1000f, 0f, 0)]
    [InlineData(10f, 0f, 0f, 0)]
    [InlineData(-5f, 1000f, 0f, 0)]
    public void CalculateFontSizeFromPercentage_ScalesWithPosterHeight(
        float percentage, float posterHeight, float margin, int expected)
    {
        Assert.Equal(expected, FontUtils.CalculateFontSizeFromPercentage(percentage, posterHeight, margin));
    }

    [Fact]
    public void GetFontStyle_MapsKnownNamesAndDefaultsToNormal()
    {
        Assert.Equal(SKFontStyle.Bold, FontUtils.GetFontStyle("Bold"));
        Assert.Equal(SKFontStyle.Italic, FontUtils.GetFontStyle("italic"));
        Assert.Equal(SKFontStyle.BoldItalic, FontUtils.GetFontStyle("Bold Italic"));
        Assert.Equal(SKFontStyle.Normal, FontUtils.GetFontStyle("something else"));
    }


    /// <summary>
    /// Every offered font style is one the parser actually understands, and they map to distinct
    /// faces: the list and the parser sit next to each other so they cannot drift apart.
    /// </summary>
    [Fact]
    public void FontStyles_AreAllUnderstoodAndDistinct()
    {
        var resolved = FontUtils.FontStyles.Select(FontUtils.GetFontStyle).ToList();

        Assert.Equal(FontUtils.FontStyles.Count, resolved.Distinct().Count());
        Assert.Equal(SKFontStyle.Bold, FontUtils.GetFontStyle("Bold"));
        Assert.Equal(SKFontStyle.BoldItalic, FontUtils.GetFontStyle("Bold Italic"));
        Assert.Equal(SKFontStyle.Normal, FontUtils.GetFontStyle("Normal"));
    }
}
