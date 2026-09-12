using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services.Posters;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// How a frame is taken from a video and cleaned up is one decision for the server. It used to be a
/// copy on every design and every profile's backdrop, which let the same decision disagree with
/// itself depending on which image was being made.
/// </summary>
public class FrameExtractionSettingsTests
{
    [Fact]
    public void TheServersSettingsReplaceWhateverADesignStillCarries()
    {
        var design = new PosterSettings
        {
            ExtractWindowStart = 1,
            ExtractWindowEnd = 2,
            BrightenHDR = 3,
            EnableLetterboxDetection = false,
            LetterboxBlackThreshold = 4,
            LetterboxConfidence = 5
        };

        var server = new FrameExtractionSettings
        {
            ExtractWindowStart = 30,
            ExtractWindowEnd = 70,
            BrightenFrame = 10,
            EnableLetterboxDetection = true,
            LetterboxBlackThreshold = 20,
            LetterboxConfidence = 90
        };

        StandardPosterGenerator.ApplyFrameExtraction(design, server);

        Assert.Equal(30, design.ExtractWindowStart);
        Assert.Equal(70, design.ExtractWindowEnd);
        Assert.Equal(10, design.BrightenHDR);
        Assert.True(design.EnableLetterboxDetection);
        Assert.Equal(20, design.LetterboxBlackThreshold);
        Assert.Equal(90, design.LetterboxConfidence);
    }

    /// <summary>
    /// A design keeps everything that is genuinely its own.
    /// </summary>
    [Fact]
    public void NothingElseOnTheDesignIsTouched()
    {
        var design = new PosterSettings { PosterStyle = PosterStyle.Bloom, PosterSafeArea = 12 };

        StandardPosterGenerator.ApplyFrameExtraction(design, new FrameExtractionSettings());

        Assert.Equal(PosterStyle.Bloom, design.PosterStyle);
        Assert.Equal(12, design.PosterSafeArea);
    }

    /// <summary>
    /// The backdrop settings carry only what framing a backdrop actually needs.
    /// </summary>
    [Fact]
    public void ABackdropCarriesOnlyItsShape()
    {
        var names = typeof(BackdropSettings)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        Assert.Equal(new[] { nameof(BackdropSettings.AspectRatio) }, names);
    }

    /// <summary>
    /// No design setting may name one of these again, or the page would offer a control that the
    /// server's value overwrites before anything is drawn.
    /// </summary>
    [Fact]
    public void TheMovedSettingsAreNoLongerOfferedOnADesign()
    {
        var labelled = SettingOptions.Text().Keys;

        foreach (var moved in new[]
                 {
                     nameof(PosterSettings.ExtractWindowStart),
                     nameof(PosterSettings.ExtractWindowEnd),
                     nameof(PosterSettings.BrightenHDR),
                     nameof(PosterSettings.EnableLetterboxDetection),
                     nameof(PosterSettings.LetterboxBlackThreshold),
                     nameof(PosterSettings.LetterboxConfidence)
                 })
        {
            Assert.DoesNotContain(moved, labelled);
        }
    }

    [Fact]
    public void TheServerSettingsCarryTheirOwnWording()
    {
        var text = SettingOptions.FrameExtractionText();

        Assert.Equal(6, text.Count);
        Assert.All(text.Values, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Label));
            Assert.False(string.IsNullOrWhiteSpace(entry.Description));
        });
    }
}
