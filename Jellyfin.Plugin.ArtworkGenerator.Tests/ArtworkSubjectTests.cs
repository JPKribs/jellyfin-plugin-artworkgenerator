using Jellyfin.Plugin.ArtworkGenerator.Models;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

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

        Assert.Equal("Pilot", subject.Primary);
        Assert.Equal(5, subject.FeaturedNumber);
        Assert.Equal("S02E05", subject.SecondaryShort);
        Assert.Equal("SEASON 2 • EPISODE 5", subject.Secondary);
        Assert.Equal(new[] { 2, 5 }, subject.SecondaryParts);
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
        Assert.Equal("SEASON 2", subject.Primary);
        Assert.Empty(subject.Secondary);
        Assert.Empty(subject.SecondaryShort);
        Assert.Equal(2, subject.FeaturedNumber);
        Assert.Empty(subject.SecondaryParts);
    }

    /// <summary>
    /// A design that draws no primary line has nothing to promote into, so the subtitle stays the
    /// subtitle rather than vanishing.
    /// </summary>
    [Fact]
    public void Season_WithNoTitleDrawn_KeepsItsSubtitle()
    {
        var subject = Subject(ArtworkItemKind.Season);
        subject.PrimaryShown = false;

        Assert.Null(subject.Primary);
        Assert.Equal("SEASON 2", subject.Secondary);
        Assert.Equal("S02", subject.SecondaryShort);
    }

    /// <summary>
    /// An episode always has a name of its own, so nothing is promoted and both lines are drawn.
    /// </summary>
    [Fact]
    public void Episode_KeepsBothLines()
    {
        var subject = Subject(ArtworkItemKind.Episode);

        Assert.Equal("Pilot", subject.Primary);
        Assert.Equal("SEASON 2 • EPISODE 5", subject.Secondary);
        Assert.Equal("S02E05", subject.SecondaryShort);
    }

    [Fact]
    public void Season_WithItsOwnName_IsHeadlinedByItWithTheNumberBeneath()
    {
        var subject = Subject(ArtworkItemKind.Season);
        subject.SeasonName = "The Crown Jewels";

        Assert.True(subject.HasCustomSeasonName);
        Assert.Equal("The Crown Jewels", subject.Primary);
        Assert.Equal("SEASON 2", subject.Secondary);
        Assert.Equal("S02", subject.SecondaryShort);
    }

    /// <summary>
    /// A series poster shows only its name: no season count and no year.
    /// </summary>
    [Fact]
    public void Series_ShowsOnlyItsName()
    {
        var subject = Subject(ArtworkItemKind.Series);
        subject.SeasonCount = 3;

        Assert.Equal("The Show", subject.Primary);
        Assert.Null(subject.FeaturedNumber);
        Assert.Equal(string.Empty, subject.Secondary);
        Assert.Equal(string.Empty, subject.SecondaryShort);
        Assert.Null(subject.ProgressPosition);
        Assert.Empty(subject.SecondaryParts);
    }

    [Fact]
    public void Series_CutoutPunchesTheName()
    {
        var subject = Subject(ArtworkItemKind.Series);

        Assert.True(subject.CutoutIsPrimary);
        Assert.Equal("THE SHOW", subject.CutoutText(CutoutType.Code));
        Assert.False(Subject(ArtworkItemKind.Episode).CutoutIsPrimary);
    }

    [Fact]
    public void CutoutText_SpellsOutTheFeaturedNumber()
    {
        Assert.Equal("FIVE", Subject(ArtworkItemKind.Episode).CutoutText(CutoutType.Text));
        Assert.Equal("TWO", Subject(ArtworkItemKind.Season).CutoutText(CutoutType.Text));
        Assert.Equal("S02E05", Subject(ArtworkItemKind.Episode).CutoutText(CutoutType.Code));
    }


    /// <summary>
    /// A film's title is its primary line and its year the secondary one, so a style draws it the
    /// same way it draws anything else without knowing it is a film.
    /// </summary>
    [Fact]
    public void Movie_DrawsItsTitleOverItsYear()
    {
        var subject = new ArtworkSubject
        {
            Kind = ArtworkItemKind.Movie,
            SeriesName = "Blade Runner",
            ProductionYear = 1982
        };

        Assert.Equal("Blade Runner", subject.Primary);
        Assert.Equal("1982", subject.Secondary);
        Assert.Equal("1982", subject.SecondaryShort);
    }

    /// <summary>A film has no number, so numeric styles have nothing to feature.</summary>
    [Fact]
    public void Movie_HasNoFeaturedNumber()
    {
        var subject = new ArtworkSubject { Kind = ArtworkItemKind.Movie, SeriesName = "Alien" };

        Assert.Null(subject.FeaturedNumber);
        Assert.Empty(subject.SecondaryParts);
    }

    /// <summary>
    /// Like a series, a film's name is its whole identity, so a cutout punches the name rather than
    /// a code, and does not then draw it again.
    /// </summary>
    [Fact]
    public void Movie_PunchesItsNameOutOfACutout()
    {
        var subject = new ArtworkSubject { Kind = ArtworkItemKind.Movie, SeriesName = "Arrival", ProductionYear = 2016 };

        Assert.True(subject.CutoutIsPrimary);
        Assert.Equal("ARRIVAL", subject.CutoutText(CutoutType.Code));
    }

    /// <summary>A film with no year recorded simply has no secondary line.</summary>
    [Fact]
    public void Movie_WithoutAYearHasNoSecondaryLine()
    {
        var subject = new ArtworkSubject { Kind = ArtworkItemKind.Movie, SeriesName = "Untitled" };

        Assert.Equal("Untitled", subject.Primary);
        Assert.Empty(subject.Secondary);
    }
}
