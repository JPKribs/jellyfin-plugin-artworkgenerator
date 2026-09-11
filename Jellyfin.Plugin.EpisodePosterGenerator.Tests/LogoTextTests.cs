using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Utilities;
using Xunit;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Tests;

/// <summary>
/// Tests for choosing and cleaning a generated logo's text.
/// </summary>
public class LogoTextTests
{
    /// <summary>
    /// A bare number at the end of a title can be part of the name, so only a parenthesized year
    /// is removed from titles.
    /// </summary>
    [Theory]
    [InlineData("Andor (2022)", "Andor")]
    [InlineData("Blade Runner 2049", "Blade Runner 2049")]
    public void Clean_StripsOnlyParenthesizedYearsFromTitles(string raw, string expected)
    {
        Assert.Equal(expected, LogoText.Clean(raw, new LogoSettings(), isFolderName: false));
    }

    [Theory]
    [InlineData("Smiling Friends (2020) [tvdbid-379403]", "Smiling Friends")]
    [InlineData("Some Show 2019 {imdb-tt1234567}", "Some Show")]
    public void Clean_StripsTagsAndYearsFromFolderNames(string raw, string expected)
    {
        Assert.Equal(expected, LogoText.Clean(raw, new LogoSettings(), isFolderName: true));
    }

    [Fact]
    public void Clean_CutsAtASeparatorWhenAsked()
    {
        var settings = new LogoSettings { CutAtSeparator = true };
        Assert.Equal("Star Wars", LogoText.Clean("Star Wars: Andor", settings, isFolderName: false));
    }

    [Fact]
    public void Clean_AppliesACustomPattern()
    {
        var settings = new LogoSettings { CustomRegex = @"^The\s+" };
        Assert.Equal("Office", LogoText.Clean("The Office", settings, isFolderName: false));
    }

    [Fact]
    public void Clean_IgnoresAnInvalidPattern()
    {
        var settings = new LogoSettings { CustomRegex = "(" };
        Assert.Equal("The Office", LogoText.Clean("The Office", settings, isFolderName: false));
    }

    [Fact]
    public void Clean_KeepsTheOriginalWhenTheRulesRemoveEverything()
    {
        var settings = new LogoSettings { CustomRegex = ".+" };
        Assert.Equal("Andor", LogoText.Clean("Andor", settings, isFolderName: false));
    }

    [Fact]
    public void Clean_UppercasesWhenAsked()
    {
        var settings = new LogoSettings { Uppercase = true };
        Assert.Equal("ANDOR", LogoText.Clean("Andor", settings, isFolderName: false));
    }

    [Fact]
    public void Resolve_FallsBackToTheSeriesNameWhenTheSourceIsEmpty()
    {
        var subject = new ArtworkSubject { SeriesName = "Andor" };
        var settings = new LogoSettings { TitleSource = LogoTitleSource.OriginalTitle };

        Assert.Equal("Andor", LogoText.Resolve(subject, settings));
    }

    [Fact]
    public void Resolve_CleansAFolderName()
    {
        var subject = new ArtworkSubject { SeriesName = "Andor", FolderName = "Andor (2022) [tvdbid-393189]" };
        var settings = new LogoSettings { TitleSource = LogoTitleSource.FolderName };

        Assert.Equal("Andor", LogoText.Resolve(subject, settings));
    }
}
