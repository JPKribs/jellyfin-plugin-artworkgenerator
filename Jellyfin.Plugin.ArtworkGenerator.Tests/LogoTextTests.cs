using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

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

    [Theory]
    [InlineData(LogoSubtitleMode.Keep, "Star Wars: Andor", null, false)]
    [InlineData(LogoSubtitleMode.TitleOnly, "Star Wars", null, false)]
    [InlineData(LogoSubtitleMode.SubtitleOnly, "Andor", null, false)]
    [InlineData(LogoSubtitleMode.TitleLarge, "Star Wars", "Andor", false)]
    [InlineData(LogoSubtitleMode.SubtitleLarge, "Andor", "Star Wars", true)]
    public void Compose_LaysOutASubtitle(LogoSubtitleMode mode, string main, string? secondary, bool secondaryFirst)
    {
        var subject = new ArtworkSubject { SeriesName = "Star Wars: Andor" };

        var lines = LogoText.Compose(subject, new LogoSettings { SubtitleMode = mode });

        Assert.Equal(main, lines.Main);
        Assert.Equal(secondary, lines.Secondary);
        Assert.Equal(secondaryFirst, lines.SecondaryFirst);
    }

    [Fact]
    public void Compose_WithoutASeparator_DrawsTheWholeName()
    {
        var subject = new ArtworkSubject { SeriesName = "Andor" };

        var lines = LogoText.Compose(subject, new LogoSettings { SubtitleMode = LogoSubtitleMode.SubtitleLarge });

        Assert.Equal("Andor", lines.Main);
        Assert.Null(lines.Secondary);
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
    public void Clean_CasesTheLettersAsAsked()
    {
        var settings = new LogoSettings { LetterCase = LogoCase.Uppercase };
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
