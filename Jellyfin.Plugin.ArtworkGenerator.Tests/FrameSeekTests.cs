using System.Linq;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services;
using Xunit;
using SeekRange = Jellyfin.Plugin.ArtworkGenerator.Services.FrameExtractionService.SeekRange;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Tests for where frames are sought. Media segments are cut out of the extraction window, so an
/// intro, the credits, or a recap is never where a frame comes from.
/// </summary>
public class FrameSeekTests
{
    [Fact]
    public void OpenRanges_WithNothingSkipped_IsTheWindow()
    {
        var open = FrameExtractionService.OpenRanges(new SeekRange(100, 500), []);

        Assert.Equal(new[] { new SeekRange(100, 500) }, open);
    }

    [Fact]
    public void OpenRanges_CutsSegmentsOutOfTheWindow()
    {
        var open = FrameExtractionService.OpenRanges(
            new SeekRange(100, 500),
            [new SeekRange(450, 600), new SeekRange(200, 250)]);

        Assert.Equal(new[] { new SeekRange(100, 200), new SeekRange(250, 450) }, open);
    }

    /// <summary>Segments from different providers can overlap, or sit wholly outside the window.</summary>
    [Fact]
    public void OpenRanges_HandlesOverlappingAndOutsideSegments()
    {
        var open = FrameExtractionService.OpenRanges(
            new SeekRange(100, 500),
            [new SeekRange(0, 50), new SeekRange(80, 150), new SeekRange(140, 180), new SeekRange(700, 800)]);

        Assert.Equal(new[] { new SeekRange(180, 500) }, open);
    }

    /// <summary>Nothing is left when segments cover the window, which the caller treats as no segments.</summary>
    [Fact]
    public void OpenRanges_IsEmptyWhenTheWindowIsCovered()
    {
        Assert.Empty(FrameExtractionService.OpenRanges(new SeekRange(100, 500), [new SeekRange(0, 1000)]));
    }

    [Fact]
    public void GenerateSeekTime_NeverLandsInASkippedSegment()
    {
        var skipped = new[] { new SeekRange(300, 400), new SeekRange(900, 1100) };
        var open = FrameExtractionService.OpenRanges(new SeekRange(240, 960), skipped);

        foreach (var phase in new[] { 0.0, 0.37, 0.999 })
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                var seek = FrameExtractionService.GenerateSeekTime(open, attempt, phase);

                Assert.InRange(seek, 240, 960);
                Assert.DoesNotContain(skipped, s => seek > s.Start && seek < s.End);
            }
        }
    }

    /// <summary>The same phase and attempt must revisit the same timestamp, or a fixed seed means nothing.</summary>
    [Fact]
    public void GenerateSeekTime_IsDeterministicAndSpread()
    {
        var open = new[] { new SeekRange(240, 960) };

        var seeks = Enumerable.Range(0, 8).Select(a => FrameExtractionService.GenerateSeekTime(open, a, 0.25)).ToList();
        var again = Enumerable.Range(0, 8).Select(a => FrameExtractionService.GenerateSeekTime(open, a, 0.25)).ToList();

        Assert.Equal(seeks, again);
        Assert.Equal(8, seeks.Distinct().Count());
        Assert.Equal(240 + (int)(0.25 * 720), seeks[0]);
    }

    [Fact]
    public void AvoidedSegmentTypes_DefaultToEveryNamedKind()
    {
        var expected = new[]
        {
            MediaSegmentType.Intro, MediaSegmentType.Outro, MediaSegmentType.Recap,
            MediaSegmentType.Preview, MediaSegmentType.Commercial
        };

        Assert.Equal(expected, FrameExtractionService.AvoidedSegmentTypes(new FrameExtractionSettings()));
        Assert.Equal(expected, FrameExtractionService.AvoidedSegmentTypes(null));
    }

    [Fact]
    public void AvoidedSegmentTypes_AreOnlyTheOnesTicked()
    {
        var settings = new FrameExtractionSettings { AvoidRecaps = false, AvoidPreviews = false };

        Assert.Equal(
            new[] { MediaSegmentType.Intro, MediaSegmentType.Outro, MediaSegmentType.Commercial },
            FrameExtractionService.AvoidedSegmentTypes(settings));
    }

    /// <summary>Turning the setting off avoids nothing, whatever is ticked beneath it.</summary>
    [Fact]
    public void AvoidedSegmentTypes_AreEmptyWhenAvoidingIsOff()
    {
        Assert.Empty(FrameExtractionService.AvoidedSegmentTypes(new FrameExtractionSettings { AvoidMediaSegments = false }));
    }

    /// <summary>The settings page fills each checkbox's label from the model, so each needs wording.</summary>
    [Fact]
    public void TheSegmentSettingsCarryTheirOwnWording()
    {
        var text = Jellyfin.Plugin.ArtworkGenerator.Services.Posters.SettingOptions.FrameExtractionText();

        foreach (var name in new[] { "AvoidMediaSegments", "AvoidIntros", "AvoidOutros", "AvoidRecaps", "AvoidPreviews", "AvoidCommercials" })
        {
            Assert.True(text.ContainsKey(name), $"{name} has no label for the settings page.");
        }
    }
}
