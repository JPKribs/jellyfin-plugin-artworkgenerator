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
    [InlineData(TitleEdge.TopFirst, false, true)]
    [InlineData(TitleEdge.BottomFirst, true, false)]
    [InlineData(TitleEdge.AlwaysTop, false, true)]
    [InlineData(TitleEdge.AlwaysBottom, true, false)]
    public void BothLines_PutTheTitleOnTheChosenEdge(TitleEdge edge, bool titleAtBottom, bool subtitleAtBottom)
    {
        var edges = FramePosterGenerator.ResolveEdges(edge, showTitle: true);

        Assert.Equal(titleAtBottom, edges.TitleAtBottom);
        Assert.Equal(subtitleAtBottom, edges.SubtitleAtBottom);
    }

    /// <summary>
    /// A lone subtitle, such as a season named after nothing but its number, takes the edge that
    /// fills first, so it lands where a lone title would. The pinned choices leave it where it is.
    /// </summary>
    [Theory]
    [InlineData(TitleEdge.TopFirst, false)]
    [InlineData(TitleEdge.BottomFirst, true)]
    [InlineData(TitleEdge.AlwaysTop, true)]
    [InlineData(TitleEdge.AlwaysBottom, false)]
    public void WithNoTitle_TheSubtitleTakesTheFillingEdge(TitleEdge edge, bool subtitleAtBottom)
    {
        var edges = FramePosterGenerator.ResolveEdges(edge, showTitle: false);

        Assert.Equal(subtitleAtBottom, edges.SubtitleAtBottom);
    }

    /// <summary>
    /// A lone line lands in the same place whichever line it is, so a series and its seasons match.
    /// </summary>
    [Theory]
    [InlineData(TitleEdge.TopFirst)]
    [InlineData(TitleEdge.BottomFirst)]
    public void ALoneLineLandsInTheSamePlaceEitherWay(TitleEdge edge)
    {
        var loneTitle = FramePosterGenerator.ResolveEdges(edge, showTitle: true).TitleAtBottom;
        var loneSubtitle = FramePosterGenerator.ResolveEdges(edge, showTitle: false).SubtitleAtBottom;

        Assert.Equal(loneTitle, loneSubtitle);
    }
}
