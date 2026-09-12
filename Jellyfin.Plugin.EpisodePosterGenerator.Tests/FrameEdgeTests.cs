using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Services.Posters;
using Xunit;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Tests;

/// <summary>
/// Tests for which edge of a framed poster each line takes.
/// </summary>
public class FrameEdgeTests
{
    /// <summary>
    /// With both lines, the chosen edge takes the title and the other takes the subtitle.
    /// </summary>
    [Theory]
    [InlineData(TextEdge.TopFirst, false, true)]
    [InlineData(TextEdge.BottomFirst, true, false)]
    [InlineData(TextEdge.AlwaysTop, false, true)]
    [InlineData(TextEdge.AlwaysBottom, true, false)]
    public void BothLines_PutTheTitleOnTheChosenEdge(TextEdge edge, bool primaryAtBottom, bool secondaryAtBottom)
    {
        var edges = FramePosterGenerator.ResolveEdges(edge, showPrimary: true);

        Assert.Equal(primaryAtBottom, edges.PrimaryAtBottom);
        Assert.Equal(secondaryAtBottom, edges.SecondaryAtBottom);
    }

    /// <summary>
    /// A lone subtitle, such as a season named after nothing but its number, takes the edge that
    /// fills first, so it lands where a lone title would. The pinned choices leave it where it is.
    /// </summary>
    [Theory]
    [InlineData(TextEdge.TopFirst, false)]
    [InlineData(TextEdge.BottomFirst, true)]
    [InlineData(TextEdge.AlwaysTop, true)]
    [InlineData(TextEdge.AlwaysBottom, false)]
    public void WithNoTitle_TheSubtitleTakesTheFillingEdge(TextEdge edge, bool secondaryAtBottom)
    {
        var edges = FramePosterGenerator.ResolveEdges(edge, showPrimary: false);

        Assert.Equal(secondaryAtBottom, edges.SecondaryAtBottom);
    }

    /// <summary>
    /// A lone line lands in the same place whichever line it is, so a series and its seasons match.
    /// </summary>
    [Theory]
    [InlineData(TextEdge.TopFirst)]
    [InlineData(TextEdge.BottomFirst)]
    public void ALoneLineLandsInTheSamePlaceEitherWay(TextEdge edge)
    {
        var loneTitle = FramePosterGenerator.ResolveEdges(edge, showPrimary: true).PrimaryAtBottom;
        var loneSubtitle = FramePosterGenerator.ResolveEdges(edge, showPrimary: false).SecondaryAtBottom;

        Assert.Equal(loneTitle, loneSubtitle);
    }
}
