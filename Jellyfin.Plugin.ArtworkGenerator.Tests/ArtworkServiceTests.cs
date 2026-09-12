using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services.Artwork;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Tests for the shape adjustment and slot mapping the artwork pipeline is built on.
/// </summary>
public class ArtworkServiceTests
{
    /// <summary>
    /// A portrait cut from a landscape frame has to be cropped to a portrait ratio; stretching it or
    /// leaving it wide would both be wrong. A portrait ratio the design already chose is kept.
    /// </summary>
    [Theory]
    [InlineData("16:9", "2:3", "2:3")]
    [InlineData("2.35:1", "4:5", "4:5")]
    [InlineData("16:9", "16:9", "2:3")]
    [InlineData("3:4", "not a ratio", "3:4")]
    public void ShapeAdjust_PortraitAlwaysCropsToAPortraitRatio(string designRatio, string portraitRatio, string expected)
    {
        var design = new PosterSettings { PosterDimensionRatio = designRatio, PortraitDimensionRatio = portraitRatio, PosterFill = PosterFill.Original };

        var adjusted = ArtworkService.ShapeAdjust(design, ArtworkShape.Portrait);

        Assert.Equal(expected, adjusted.PosterDimensionRatio);
        Assert.Equal(PosterFill.Fit, adjusted.PosterFill);
        Assert.Equal(ArtworkShape.Portrait, adjusted.Shape);
        Assert.Equal(designRatio, design.PosterDimensionRatio);
    }

    /// <summary>
    /// Text sizes come from the short side, which in portrait is the width, so a portrait render
    /// measures them against the average of the two sides instead. A 2:3 poster therefore draws
    /// text about a quarter larger than its width alone would give. Landscape uses the sizes as set.
    /// </summary>
    [Fact]
    public void ShapeAdjust_PortraitScalesTheText()
    {
        var design = new PosterSettings { PrimaryFontSize = 10f, SecondaryFontSize = 7f };

        var portrait = ArtworkService.ShapeAdjust(design, ArtworkShape.Portrait);
        var landscape = ArtworkService.ShapeAdjust(design, ArtworkShape.Landscape);

        Assert.Equal(12.247, portrait.PrimaryFontSize, 3);
        Assert.Equal(8.573, portrait.SecondaryFontSize, 3);
        Assert.Equal(10.0, landscape.PrimaryFontSize, 3);
        Assert.Equal(10.0, design.PrimaryFontSize, 3);
    }

    [Theory]
    [InlineData("2:3", "16:9")]
    [InlineData("21:9", "21:9")]
    public void ShapeAdjust_LandscapeNeverTakesAPortraitRatio(string designRatio, string expected)
    {
        var design = new PosterSettings { PosterDimensionRatio = designRatio, PosterFill = PosterFill.Original, Shape = ArtworkShape.Portrait };

        var adjusted = ArtworkService.ShapeAdjust(design, ArtworkShape.Landscape);

        Assert.Equal(expected, adjusted.PosterDimensionRatio);
        Assert.Equal(PosterFill.Original, adjusted.PosterFill);
        Assert.Equal(ArtworkShape.Landscape, adjusted.Shape);
    }

    [Theory]
    [InlineData(ImageType.Primary, ArtworkSlot.Primary)]
    [InlineData(ImageType.Thumb, ArtworkSlot.Thumb)]
    [InlineData(ImageType.Logo, ArtworkSlot.Logo)]
    [InlineData(ImageType.Backdrop, ArtworkSlot.Backdrop)]
    public void Slots_MapToJellyfinImageTypesBothWays(ImageType type, ArtworkSlot slot)
    {
        Assert.Equal(slot, ArtworkService.GetSlot(type));
        Assert.Equal(type, ArtworkService.ToImageType(slot));
    }

    [Fact]
    public void Slots_IgnoreImageTypesThePluginNeverFills()
    {
        Assert.Null(ArtworkService.GetSlot(ImageType.Banner));
        Assert.Null(ArtworkService.GetSlot(ImageType.Disc));
    }

    [Fact]
    public void Profile_OffersOnlySlotsJellyfinClientsShow()
    {
        Assert.True(ArtworkProfile.IsSupported(ArtworkItemKind.Series, ArtworkSlot.Logo));
        Assert.False(ArtworkProfile.IsSupported(ArtworkItemKind.Season, ArtworkSlot.Logo));
        Assert.True(ArtworkProfile.IsSupported(ArtworkItemKind.Episode, ArtworkSlot.Thumb));
        Assert.False(ArtworkProfile.IsSupported(ArtworkItemKind.Episode, ArtworkSlot.Logo));
    }

    [Fact]
    public void Profile_ThumbsAreAlwaysLandscape()
    {
        var profile = new ArtworkProfile();

        Assert.Equal(ArtworkShape.Portrait, profile.GetShape(ArtworkItemKind.Series, ArtworkSlot.Primary));
        Assert.Equal(ArtworkShape.Landscape, profile.GetShape(ArtworkItemKind.Series, ArtworkSlot.Thumb));
        Assert.Equal(ArtworkShape.Landscape, profile.GetShape(ArtworkItemKind.Episode, ArtworkSlot.Primary));
        Assert.Equal(ArtworkShape.Landscape, profile.GetShape(ArtworkItemKind.Episode, ArtworkSlot.Thumb));
    }
}
