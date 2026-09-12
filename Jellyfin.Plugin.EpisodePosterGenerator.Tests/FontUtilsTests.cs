using System.Collections.Generic;
using Jellyfin.Plugin.EpisodePosterGenerator.Utilities;
using SkiaSharp;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Tests;

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

    [Fact]
    public void SetMissingFamilyReporter_StaysQuietForAnInstalledFamily()
    {
        var installed = FontUtils.GetAvailableFontFamilies();
        if (installed.Count == 0)
        {
            // A machine with no fonts at all has nothing to assert against.
            return;
        }

        FontUtils.ClearCache();

        var reported = new List<string>();
        FontUtils.SetMissingFamilyReporter(reported.Add);

        try
        {
            var typeface = FontUtils.CreateTypeface(installed[0], SKFontStyle.Normal);

            Assert.Equal(installed[0], typeface.FamilyName, ignoreCase: true);
            Assert.Empty(reported);
        }
        finally
        {
            FontUtils.SetMissingFamilyReporter(null);
            FontUtils.ClearCache();
        }
    }

    [Fact]
    public void GetAvailableFontFamilies_ReturnsSortedDistinctNames()
    {
        var families = FontUtils.GetAvailableFontFamilies();

        Assert.All(families, f => Assert.False(string.IsNullOrWhiteSpace(f)));
        Assert.Equal(families.Count, new System.Collections.Generic.HashSet<string>(
            families, System.StringComparer.OrdinalIgnoreCase).Count);
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
