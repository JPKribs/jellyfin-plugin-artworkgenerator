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
    /// A season's own identity is carried by its label, so the headline is the series.
    /// </summary>
    [Fact]
    public void Season_IsHeadlinedByTheSeries()
    {
        var subject = Subject(ArtworkItemKind.Season);

        Assert.Equal("The Show", subject.Title);
        Assert.Equal(2, subject.Number);
        Assert.Equal("S02", subject.Code);
        Assert.Equal("SEASON 2", subject.Label);
        Assert.Empty(subject.NumberParts);
        Assert.Equal("2 OF 3", subject.ProgressText);
    }

    [Fact]
    public void Season_KeepsACustomSeasonName()
    {
        var subject = Subject(ArtworkItemKind.Season);
        subject.SeasonName = "The Crown Jewels";

        Assert.Equal("THE CROWN JEWELS", subject.Label);
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
