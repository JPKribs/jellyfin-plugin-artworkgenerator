using Jellyfin.Plugin.ArtworkGenerator.Services.Posters;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Which border edge each line of a framed poster takes. The title goes where the ordinary text
/// position puts it and the subtitle takes the other edge, which replaced a setting of this design's
/// own that spoke of edges filling rather than of a title and a subtitle.
/// </summary>
public class FrameEdgeTests
{
    [Theory]
    [InlineData(LayoutAnchor.Top, false, true)]
    [InlineData(LayoutAnchor.Bottom, true, false)]
    public void BothLines_PutTheTitleWhereThePositionSaysAndTheSubtitleOpposite(
        LayoutAnchor anchor, bool primaryAtBottom, bool secondaryAtBottom)
    {
        var edges = FramePosterGenerator.ResolveEdges(anchor, loneLineFollowsTitle: true, showPrimary: true);

        Assert.Equal(primaryAtBottom, edges.PrimaryAtBottom);
        Assert.Equal(secondaryAtBottom, edges.SecondaryAtBottom);
    }

    /// <summary>
    /// A lone subtitle, such as a season named after nothing but its number, moves up into the
    /// title's edge so an item with one line looks the same whichever line it has.
    /// </summary>
    [Theory]
    [InlineData(LayoutAnchor.Top, false)]
    [InlineData(LayoutAnchor.Bottom, true)]
    public void WithNoTitle_ALoneSubtitleFollowsTheTitlesEdge(LayoutAnchor anchor, bool secondaryAtBottom)
    {
        var edges = FramePosterGenerator.ResolveEdges(anchor, loneLineFollowsTitle: true, showPrimary: false);

        Assert.Equal(secondaryAtBottom, edges.SecondaryAtBottom);
    }

    /// <summary>
    /// Switched off, the subtitle keeps its own edge and the title's is left empty.
    /// </summary>
    [Theory]
    [InlineData(LayoutAnchor.Top, true)]
    [InlineData(LayoutAnchor.Bottom, false)]
    public void WithNoTitle_ALoneSubtitleCanStayOnItsOwnEdge(LayoutAnchor anchor, bool secondaryAtBottom)
    {
        var edges = FramePosterGenerator.ResolveEdges(anchor, loneLineFollowsTitle: false, showPrimary: false);

        Assert.Equal(secondaryAtBottom, edges.SecondaryAtBottom);
    }

    [Theory]
    [InlineData(LayoutAnchor.Top)]
    [InlineData(LayoutAnchor.Bottom)]
    public void ALoneLineLandsInTheSamePlaceEitherWay(LayoutAnchor anchor)
    {
        var loneTitle = FramePosterGenerator.ResolveEdges(anchor, true, showPrimary: true).PrimaryAtBottom;
        var loneSubtitle = FramePosterGenerator.ResolveEdges(anchor, true, showPrimary: false).SecondaryAtBottom;

        Assert.Equal(loneTitle, loneSubtitle);
    }
}
