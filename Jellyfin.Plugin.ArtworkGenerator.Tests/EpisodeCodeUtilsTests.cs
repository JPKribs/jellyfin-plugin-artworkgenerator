using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Tests for the episode code and label formatting in <see cref="EpisodeCodeUtils"/>.
/// </summary>
public class EpisodeCodeUtilsTests
{
    [Theory]
    [InlineData(1, 5, "S01E05")]
    [InlineData(12, 7, "S12E07")]
    [InlineData(1, 100, "S01E100")]
    [InlineData(1, 1000, "S01E1000")]
    [InlineData(2026, 5, "S2026E05")]
    public void FormatEpisodeCode_PadsByDigitCount(int season, int episode, string expected)
    {
        Assert.Equal(expected, EpisodeCodeUtils.FormatEpisodeCode(season, episode));
    }

    [Fact]
    public void FormatEpisodeText_Code_UsesEpisodeCode()
    {
        Assert.Equal("S01E05", EpisodeCodeUtils.FormatEpisodeText(CutoutType.Code, 1, 5));
    }

    [Fact]
    public void FormatEpisodeText_Text_SmallNumberIsSpelledOut()
    {
        Assert.Equal("FIVE", EpisodeCodeUtils.FormatEpisodeText(CutoutType.Text, 1, 5));
    }

    [Fact]
    public void FormatEpisodeText_Text_LargeNumberStaysNumeric()
    {
        // At or above 100 the spelled out form is dropped in favor of the digits.
        Assert.Equal("150", EpisodeCodeUtils.FormatEpisodeText(CutoutType.Text, 1, 150));
    }

    [Theory]
    [InlineData(2, 5, true, true, "SEASON 2 • EPISODE 5")]
    [InlineData(2, 5, true, false, "SEASON 2")]
    [InlineData(2, 5, false, true, "EPISODE 5")]
    [InlineData(2, 5, false, false, "")]
    public void FormatFullText_HonorsInclusionFlags(int season, int episode, bool includeSeason, bool includeEpisode, string expected)
    {
        Assert.Equal(expected, EpisodeCodeUtils.FormatFullText(season, episode, includeSeason, includeEpisode));
    }
}
