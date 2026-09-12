using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Xunit;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Tests;

/// <summary>
/// Tests for how <see cref="ArtworkSubject"/> describes each kind of item to the poster styles.
/// </summary>
public class ArtworkSubjectTests
{
    private static ArtworkSubject Subject(ArtworkItemKind kind) => new()
    {
        Kind = kind,
        SeriesName = "The Show",
        EpisodeName = "Pilot",
        SeasonNumber = 2,
        SeasonName = "Season 2",
        EpisodeNumberStart = 5,
        EpisodeNumberEnd = 5,
        SeasonEpisodeCount = 10,
        SeasonCount = 3,
        ProductionYear = 2024
    };

    [Fact]
    public void Episode_DescribesItselfByEpisode()
    {
        var subject = Subject(ArtworkItemKind.Episode);

        Assert.Equal("Pilot", subject.Title);
        Assert.Equal(5, subject.Number);
        Assert.Equal("S02E05", subject.Code);
        Assert.Equal("SEASON 2 • EPISODE 5", subject.Label);
        Assert.Equal(new[] { 2, 5 }, subject.NumberParts);
        Assert.Equal("5 OF 10", subject.ProgressText);
    }

    [Fact]
    public void Episode_WithoutSeasonSize_NamesTheEpisode()
    {
        var subject = Subject(ArtworkItemKind.Episode);
        subject.SeasonEpisodeCount = null;

        Assert.Null(subject.ProgressTotal);
        Assert.Equal("EPISODE 5", subject.ProgressText);
    }

    /// <summary>
    /// A season named after nothing but its number has nothing of its own to headline, so its
    /// subtitle is promoted into the primary line and the secondary line is left empty. The one
    /// thing it has to say is then said once, in the larger type.
    /// </summary>
    [Theory]
    [InlineData("Season 2")]
    [InlineData("season 02")]
    [InlineData("2")]
    public void Season_WithANumberedName_PromotesItsSubtitle(string seasonName)
    {
        var subject = Subject(ArtworkItemKind.Season);
        subject.SeasonName = seasonName;

        Assert.False(subject.HasCustomSeasonName);
        Assert.Equal("SEASON 2", subject.Title);
        Assert.Empty(subject.Label);
        Assert.Empty(subject.Code);
        Assert.Equal(2, subject.Number);
        Assert.Empty(subject.NumberParts);
    }

    /// <summary>
    /// A design that draws no primary line has nothing to promote into, so the subtitle stays the
    /// subtitle rather than vanishing.
    /// </summary>
    [Fact]
    public void Season_WithNoTitleDrawn_KeepsItsSubtitle()
    {
        var subject = Subject(ArtworkItemKind.Season);
        subject.TitleShown = false;

        Assert.Null(subject.Title);
        Assert.Equal("SEASON 2", subject.Label);
        Assert.Equal("S02", subject.Code);
    }

    /// <summary>
    /// An episode always has a name of its own, so nothing is promoted and both lines are drawn.
    /// </summary>
    [Fact]
    public void Episode_KeepsBothLines()
    {
        var subject = Subject(ArtworkItemKind.Episode);

        Assert.Equal("Pilot", subject.Title);
        Assert.Equal("SEASON 2 • EPISODE 5", subject.Label);
        Assert.Equal("S02E05", subject.Code);
    }

    [Fact]
    public void Season_WithItsOwnName_IsHeadlinedByItWithTheNumberBeneath()
    {
        var subject = Subject(ArtworkItemKind.Season);
        subject.SeasonName = "The Crown Jewels";

        Assert.True(subject.HasCustomSeasonName);
        Assert.Equal("The Crown Jewels", subject.Title);
        Assert.Equal("SEASON 2", subject.Label);
        Assert.Equal("S02", subject.Code);
    }

    /// <summary>
    /// A series poster shows only its name: no season count and no year.
    /// </summary>
    [Fact]
    public void Series_ShowsOnlyItsName()
    {
        var subject = Subject(ArtworkItemKind.Series);
        subject.SeasonCount = 3;

        Assert.Equal("The Show", subject.Title);
        Assert.Null(subject.Number);
        Assert.Equal(string.Empty, subject.Label);
        Assert.Equal(string.Empty, subject.Code);
        Assert.Null(subject.ProgressPosition);
        Assert.Empty(subject.NumberParts);
    }

    [Fact]
    public void Series_CutoutPunchesTheName()
    {
        var subject = Subject(ArtworkItemKind.Series);

        Assert.True(subject.CutoutIsTitle);
        Assert.Equal("THE SHOW", subject.CutoutText(CutoutType.Code));
        Assert.False(Subject(ArtworkItemKind.Episode).CutoutIsTitle);
    }

    [Fact]
    public void CutoutText_SpellsOutTheFeaturedNumber()
    {
        Assert.Equal("FIVE", Subject(ArtworkItemKind.Episode).CutoutText(CutoutType.Text));
        Assert.Equal("TWO", Subject(ArtworkItemKind.Season).CutoutText(CutoutType.Text));
        Assert.Equal("S02E05", Subject(ArtworkItemKind.Episode).CutoutText(CutoutType.Code));
    }
}
