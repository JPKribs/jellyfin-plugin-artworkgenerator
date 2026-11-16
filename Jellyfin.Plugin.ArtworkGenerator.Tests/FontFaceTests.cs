using Jellyfin.Plugin.ArtworkGenerator.Services.Fonts;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// A face names its style from its own weight and slant, so the Settings tab lists what each uploaded file really is.
/// </summary>
public class FontFaceTests
{
    /// <summary>
    /// Weight and slant map to the four style names, with semibold and up counted as bold.
    /// </summary>
    [Theory]
    [InlineData(400, false, "Regular")]
    [InlineData(300, false, "Regular")]
    [InlineData(600, false, "Bold")]
    [InlineData(700, false, "Bold")]
    [InlineData(400, true, "Italic")]
    [InlineData(900, true, "Bold Italic")]
    public void TheStyleComesFromWeightAndSlant(int weight, bool italic, string expected)
    {
        var face = new FontFace("Lora", weight, italic, FontSource.Uploaded, "lora.ttf", SKTypeface.Default);

        Assert.Equal(expected, face.Style);
    }

    /// <summary>
    /// A face built from a typeface reads the family and style from it.
    /// </summary>
    [Fact]
    public void FromReadsTheTypeface()
    {
        var bold = BundledFonts.Faces[0].Typeface;

        var face = FontFace.From(bold, FontSource.Bundled, "x.ttf");

        Assert.Equal(bold.FamilyName, face.Family);
        Assert.Equal(bold.FontWeight, face.Weight);
        Assert.Equal(FontSource.Bundled, face.Source);
    }
}
