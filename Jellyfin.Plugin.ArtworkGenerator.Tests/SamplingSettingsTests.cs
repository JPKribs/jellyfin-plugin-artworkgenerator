using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Serialization;
using Jellyfin.Plugin.ArtworkGenerator.Configuration;
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

        Assert.Equal((4, 8, 48), settings.Sampling());
        Assert.Equal(FrameExtractionSettings.Preset(SamplingRate.Medium), settings.Sampling());
        Assert.True(settings.ReuseSamples);
        Assert.Equal(2, settings.ParallelExtractions);
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(3, 12)]
    [InlineData(10, 40)]
    [InlineData(20, 48)]
    public void TheDefaultsSampleAsMediumDid(int count, int expected)
    {
        Assert.Equal(expected, FrameExtractionService.MaxAttempts(count, new FrameExtractionSettings()));
    }

    /// <summary>
    /// A pool spread over several videos ranks their frames against one another, so a video asked
    /// for one frame takes just the samples per frame rather than a field of its own.
    /// </summary>
    [Theory]
    [InlineData(SamplingRate.Low, 1, 2)]
    [InlineData(SamplingRate.Medium, 1, 4)]
    [InlineData(SamplingRate.High, 1, 8)]
    [InlineData(SamplingRate.Medium, 3, 12)]
    [InlineData(SamplingRate.Medium, 20, 48)]
    public void ASpreadPoolSamplesAtTheRateItPromises(SamplingRate rate, int count, int expected)
    {
        var (perFrame, minimum, maximum) = FrameExtractionSettings.Preset(rate);

        Assert.Equal(expected, FrameExtractionService.MaxAttempts(count, perFrame, minimum, maximum, spread: true));
    }

    /// <summary>One sample per frame and a minimum of one takes exactly the frames asked for.</summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(12, 12)]
    [InlineData(100, 64)]
    public void TheFewestSamplesTakeOnePerFrame(int count, int expected)
    {
        var settings = new FrameExtractionSettings { SamplesPerFrame = 1, MinimumSamplesPerVideo = 1, MaximumSamplesPerVideo = 64 };

        Assert.Equal(expected, FrameExtractionService.MaxAttempts(count, settings));
    }

    /// <summary>
    /// The numbers are typed by hand, so anything out of range is pulled back into it, and a minimum
    /// above the maximum gives way to it.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 0, 1, 1, 1)]
    [InlineData(99, 99, 99, 8, 64, 64)]
    [InlineData(4, 30, 10, 4, 10, 10)]
    [InlineData(-3, -3, -3, 1, 1, 1)]
    public void TheSamplingNumbersAreKeptInRange(int perFrame, int minimum, int maximum, int expectedPerFrame, int expectedMinimum, int expectedMaximum)
    {
        var settings = new FrameExtractionSettings { SamplesPerFrame = perFrame, MinimumSamplesPerVideo = minimum, MaximumSamplesPerVideo = maximum };

        Assert.Equal((expectedPerFrame, expectedMinimum, expectedMaximum), settings.Sampling());
    }

    [Fact]
    public void NoSettingOutrunsTheFramePoolsGrowthStride()
    {
        var settings = new FrameExtractionSettings { SamplesPerFrame = int.MaxValue, MinimumSamplesPerVideo = int.MaxValue, MaximumSamplesPerVideo = int.MaxValue };

        Assert.True(FrameExtractionService.MaxAttempts(int.MaxValue / 16, settings) <= 64);
    }

    /// <summary>
    /// A configuration saved with a Sampling Rate preset keeps sampling exactly as it did: the
    /// preset becomes the three numbers it stood for, and is never written back.
    /// </summary>
    [Theory]
    [InlineData(SamplingRate.Low, 2, 4, 24)]
    [InlineData(SamplingRate.Medium, 4, 8, 48)]
    [InlineData(SamplingRate.High, 8, 16, 64)]
    public void AnOldPresetBecomesTheNumbersItStoodFor(SamplingRate rate, int perFrame, int minimum, int maximum)
    {
        var xml = $"<FrameExtractionSettings><SamplingRate>{rate}</SamplingRate></FrameExtractionSettings>";
        var settings = (FrameExtractionSettings)new XmlSerializer(typeof(FrameExtractionSettings)).Deserialize(new StringReader(xml))!;

        Assert.True(settings.UpgradeLegacySampling());
        Assert.Equal((perFrame, minimum, maximum), settings.Sampling());
        Assert.Null(settings.SamplingRate);

        var written = new StringWriter();
        new XmlSerializer(typeof(FrameExtractionSettings)).Serialize(written, settings);
        Assert.DoesNotContain("<SamplingRate", written.ToString(), StringComparison.Ordinal);
        Assert.Contains($"<SamplesPerFrame>{perFrame}</SamplesPerFrame>", written.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The upgrade runs whenever a configuration is normalized, at load and on save.</summary>
    [Fact]
    public void NormalizingAConfigurationUpgradesItsPreset()
    {
        var config = new PluginConfiguration { FrameExtraction = new FrameExtractionSettings { SamplingRate = SamplingRate.Low } };

        PosterConfigurationService.Normalize(config);

        Assert.Equal((2, 4, 24), config.FrameExtraction.Sampling());
    }

    [Fact]
    public void ANewConfigurationHasNoPresetToUpgrade()
    {
        Assert.False(new FrameExtractionSettings().UpgradeLegacySampling());
    }

    /// <summary>The page edits the three numbers; the old preset is never sent to it.</summary>
    [Fact]
    public void ThePageGetsTheNumbersAndNotThePreset()
    {
        var json = JsonSerializer.Serialize(new FrameExtractionSettings { SamplingRate = SamplingRate.High });

        Assert.DoesNotContain("SamplingRate", json, StringComparison.Ordinal);
        Assert.Contains("\"SamplesPerFrame\":4", json, StringComparison.Ordinal);
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
    public void NoFrameExtractionSettingIsAListOfChoicesAnyMore()
    {
        Assert.Empty(SettingOptions.FrameExtractionOptions());
    }
}
