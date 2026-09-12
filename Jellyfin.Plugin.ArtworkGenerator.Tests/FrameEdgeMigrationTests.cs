using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services;
using Jellyfin.Plugin.ArtworkGenerator.Services.Posters;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// A framed design used to name its own border edges. Saved configurations still carry that name,
/// so it has to arrive as the ordinary text position plus the lone line setting, and land on the
/// same layout it did before.
/// </summary>
public class FrameEdgeMigrationTests
{
    [Theory]
    [InlineData(TextEdge.TopFirst, TextPosition.Top, true)]
    [InlineData(TextEdge.BottomFirst, TextPosition.Bottom, true)]
    [InlineData(TextEdge.AlwaysTop, TextPosition.Top, false)]
    [InlineData(TextEdge.AlwaysBottom, TextPosition.Bottom, false)]
    public void TheOldEdgeChoiceArrivesAsAPositionAndALoneLineRule(
        TextEdge edge, TextPosition position, bool loneLineFollowsTitle)
    {
        var settings = new PosterSettings { TextEdge = edge };

        Assert.True(PosterConfigurationService.MigrateTextVocabulary(settings));

        Assert.Equal(position, settings.TextPosition);
        Assert.Equal(loneLineFollowsTitle, settings.LoneLineFollowsTitle);

        // Cleared, so it is written back under the current name only.
        Assert.Null(settings.TextEdge);
    }

    /// <summary>
    /// The layout each migrated choice produces has to match what the old one drew, for an item
    /// with both lines and for one carrying only a subtitle.
    /// </summary>
    [Theory]
    [InlineData(TextEdge.TopFirst)]
    [InlineData(TextEdge.BottomFirst)]
    [InlineData(TextEdge.AlwaysTop)]
    [InlineData(TextEdge.AlwaysBottom)]
    public void TheMigratedLayoutMatchesWhatTheOldChoiceDrew(TextEdge edge)
    {
        var settings = new PosterSettings { TextEdge = edge };
        PosterConfigurationService.MigrateTextVocabulary(settings);

        var anchor = settings.TextPosition == TextPosition.Bottom
            ? Utilities.LayoutAnchor.Bottom
            : Utilities.LayoutAnchor.Top;

        // What the superseded rule produced, kept here so the two can be compared directly.
        static (bool Primary, bool Secondary) Superseded(TextEdge e, bool showPrimary)
        {
            var fillsBottom = e is TextEdge.BottomFirst or TextEdge.AlwaysBottom;
            var pinned = e is TextEdge.AlwaysTop or TextEdge.AlwaysBottom;
            return (fillsBottom, pinned || showPrimary ? !fillsBottom : fillsBottom);
        }

        foreach (var showPrimary in new[] { true, false })
        {
            var now = FramePosterGenerator.ResolveEdges(anchor, settings.LoneLineFollowsTitle, showPrimary);
            var before = Superseded(edge, showPrimary);

            Assert.Equal(before.Primary, now.PrimaryAtBottom);
            Assert.Equal(before.Secondary, now.SecondaryAtBottom);
        }
    }

    /// <summary>
    /// The plugin configuration is XML, not JSON, and the old name is now a nullable enum. If that
    /// stopped deserializing, every saved framed design would quietly fall back to the default edge
    /// with nothing to show it had happened.
    /// </summary>
    [Fact]
    public void TheOldNameStillLoadsFromASavedXmlConfiguration()
    {
        const string Xml = "<PosterSettings><PosterStyle>Frame</PosterStyle><TextEdge>AlwaysBottom</TextEdge></PosterSettings>";

        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(PosterSettings));
        using var reader = new System.IO.StringReader(Xml);
        var settings = (PosterSettings)serializer.Deserialize(reader)!;

        Assert.Equal(TextEdge.AlwaysBottom, settings.TextEdge);

        PosterConfigurationService.MigrateTextVocabulary(settings);

        Assert.Equal(TextPosition.Bottom, settings.TextPosition);
        Assert.False(settings.LoneLineFollowsTitle);
    }
}
