using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services;
using Jellyfin.Plugin.ArtworkGenerator.Services.Artwork;
using Jellyfin.Plugin.ArtworkGenerator.Services.Posters;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

public class SamplingSettingsTests
{
    [Fact]
    public void TheDefaultsKeepTheCurrentBehavior()
    {
        var settings = new FrameExtractionSettings();

        Assert.Equal(SamplingRate.Medium, settings.SamplingRate);
        Assert.True(settings.ReuseSamples);
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(3, 12)]
    [InlineData(10, 40)]
    [InlineData(20, 48)]
    public void MediumSamplesAsBefore(int count, int expected)
    {
        Assert.Equal(expected, FrameExtractionService.MaxAttempts(count, SamplingRate.Medium));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    [InlineData(50)]
    public void LowSamplesLessAndHighSamplesMore(int count)
    {
        var low = FrameExtractionService.MaxAttempts(count, SamplingRate.Low);
        var medium = FrameExtractionService.MaxAttempts(count, SamplingRate.Medium);
        var high = FrameExtractionService.MaxAttempts(count, SamplingRate.High);

        Assert.True(low < medium);
        Assert.True(medium < high);
    }

    [Fact]
    public void NoRateOutrunsTheFramePoolsGrowthStride()
    {
        Assert.True(FrameExtractionService.MaxAttempts(int.MaxValue / 16, SamplingRate.High) <= 64);
    }

    private static readonly ArtworkSlot[] Slots = { ArtworkSlot.Primary, ArtworkSlot.Thumb, ArtworkSlot.Logo, ArtworkSlot.Backdrop };

    [Theory]
    [InlineData(ArtworkSlot.Primary, 0)]
    [InlineData(ArtworkSlot.Backdrop, 1)]
    [InlineData(ArtworkSlot.Thumb, 2)]
    [InlineData(ArtworkSlot.Logo, 3)]
    public void ReusedSamplesOverlapOneRankApart(ArtworkSlot slot, int expected)
    {
        Assert.Equal(expected, ArtworkService.RankOffset(slot, 10, picking: true, reuseSamples: true));
        Assert.Equal(expected, ArtworkService.RankOffset(slot, 1, picking: false, reuseSamples: true));
    }

    [Theory]
    [InlineData(ArtworkSlot.Primary, 0)]
    [InlineData(ArtworkSlot.Thumb, 10)]
    [InlineData(ArtworkSlot.Logo, 20)]
    [InlineData(ArtworkSlot.Backdrop, 30)]
    public void UnreusedSamplesGiveEveryTypeItsOwnBlockInThePicker(ArtworkSlot slot, int expected)
    {
        Assert.Equal(expected, ArtworkService.RankOffset(slot, 10, picking: true, reuseSamples: false));
    }

    [Fact]
    public void UnreusedBlocksNeverOverlapInThePicker()
    {
        var ranks = Slots
            .SelectMany(slot => Enumerable.Range(ArtworkService.RankOffset(slot, 10, picking: true, reuseSamples: false), 10))
            .ToList();

        Assert.Equal(ranks.Count, ranks.Distinct().Count());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(20)]
    public void UnreusedBlocksNeverOverlapInARefreshWhateverTheBackdropLimit(int backdropLimit)
    {
        var ranks = Slots
            .SelectMany(slot =>
            {
                var count = slot == ArtworkSlot.Backdrop ? backdropLimit : 1;
                return Enumerable.Range(ArtworkService.RankOffset(slot, count, picking: false, reuseSamples: false), count);
            })
            .ToList();

        Assert.Equal(ranks.Count, ranks.Distinct().Count());
    }

    [Fact]
    public void AHugeCountCannotOverflowTheOffset()
    {
        Assert.True(ArtworkService.RankOffset(ArtworkSlot.Backdrop, int.MaxValue, picking: true, reuseSamples: false) > 0);
    }

    [Fact]
    public void TheSamplingRateChoicesAreServedToThePage()
    {
        var options = SettingOptions.FrameExtractionOptions();

        Assert.Equal(new[] { "Low", "Medium", "High" }, options["SamplingRate"].Select(o => o.Value));
    }
}
