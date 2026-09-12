using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services.Artwork;
using MediaBrowser.Model.Drawing;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Tests for ordering a poster slot's designs in the Edit Images picker.
/// </summary>
public class ArtworkInterleaveTests
{
    private static GeneratedArtwork Image(int width) => new(new byte[] { 1 }, "image/jpeg", ImageFormat.Jpg, width, 1);

    [Fact]
    public void InterleaveByFrame_GroupsEveryDesignsVersionOfAFrameTogether()
    {
        var first = new GeneratedArtwork?[] { Image(10), Image(11), Image(12) };
        var second = new GeneratedArtwork?[] { Image(20), Image(21), Image(22) };

        var widths = ArtworkService.InterleaveByFrame(new[] { first, second }).Select(a => a.Width);

        Assert.Equal(new[] { 10, 20, 11, 21, 12, 22 }, widths);
    }

    [Fact]
    public void InterleaveByFrame_KeepsFramesAlignedPastAFailureAndAShorterDesign()
    {
        var first = new GeneratedArtwork?[] { Image(10), null, Image(12) };
        var backdrop = new GeneratedArtwork?[] { Image(20) };
        var third = new GeneratedArtwork?[] { Image(30), Image(31), Image(32) };

        var widths = ArtworkService.InterleaveByFrame(new[] { first, backdrop, third }).Select(a => a.Width);

        Assert.Equal(new[] { 10, 20, 30, 31, 12, 32 }, widths);
    }
}
