using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services.Posters;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// A season whose name is just its number, worded some other way, ends up saying the same thing
/// twice: "Staffel 12" as the title and "SEASON 12" as the label. This setting drops the repeat,
/// and only the repeat. A season with a name of its own keeps both lines.
/// </summary>
public class RepeatedSubtitleTests
{
    private static bool DrawsSubtitle(string seasonName, bool hideRepeat)
    {
        var subject = new ArtworkSubject
        {
            Kind = ArtworkItemKind.Season,
            SeriesName = "A Show",
            SeasonName = seasonName,
            SeasonNumber = 12
        };

        var settings = new PosterSettings { ShowSecondary = true, HideRepeatedSubtitle = hideRepeat };

        return StandardPosterGenerator.DrawsSecondary(settings, subject);
    }

    /// <summary>
    /// The plain case needs no setting: a season named "Season 12" has no name of its own, so the
    /// label is promoted into the title and there was never a second line to drop.
    /// </summary>
    [Fact]
    public void APlainlyNumberedSeasonNeverHadASecondLine()
    {
        Assert.False(DrawsSubtitle("Season 12", hideRepeat: false));
        Assert.False(DrawsSubtitle("Season 12", hideRepeat: true));
    }

    [Theory]
    [InlineData("Staffel 12")]
    [InlineData("Series 12")]
    [InlineData("Temporada 12")]
    [InlineData("Saison 012")]
    public void ANumberingWordedAnotherWayIsDroppedWhenAsked(string seasonName)
    {
        Assert.True(DrawsSubtitle(seasonName, hideRepeat: false));
        Assert.False(DrawsSubtitle(seasonName, hideRepeat: true));
    }

    [Theory]
    [InlineData("The Glump Saga")]
    [InlineData("12 Monkeys")]
    [InlineData("Staffel 9")]
    public void ANameOfItsOwnKeepsBothLines(string seasonName)
    {
        Assert.True(DrawsSubtitle(seasonName, hideRepeat: true));
    }

    [Fact]
    public void TheSettingIsOffByDefault()
    {
        Assert.False(new PosterSettings().HideRepeatedSubtitle);
    }
}
