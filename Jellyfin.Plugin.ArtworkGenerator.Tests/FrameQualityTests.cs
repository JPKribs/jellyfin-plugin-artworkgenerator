using System;
using Jellyfin.Plugin.ArtworkGenerator.Services;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// The frame scorer ranks candidates rather than gating them. Its predecessor compared brightness
/// and sharpness against pass marks so low that almost every real frame cleared both and scored a
/// perfect 1.000, which made "the best frame" mean "the first one sampled". These hold the
/// properties that distinction depends on.
/// </summary>
public class FrameQualityTests
{
    private static SKBitmap Frame(int width, int height, Func<int, int, SKColor> shade)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, shade(x, y));
            }
        }

        return bitmap;
    }

    private static SKColor Grey(byte v) => new(v, v, v);

    private static double Score(SKBitmap frame)
    {
        using var analysis = FrameExtractionService.CreateAnalysisBitmap(frame);
        return FrameExtractionService.CalculateQualityScore(FrameExtractionService.AnalyzeFrame(analysis));
    }

    /// <summary>
    /// A blown out frame is as useless as a crushed one, and only the crushed one was ever
    /// penalised: the old score rose monotonically with brightness and topped out at 5% grey.
    /// </summary>
    [Fact]
    public void ABlownOutFrameScoresBelowAMidToneOne()
    {
        using var midTone = Frame(160, 90, (x, y) => Grey((byte)(90 + ((x * 7 + y * 5) % 80))));
        using var blownOut = Frame(160, 90, (x, y) => Grey((byte)(230 + ((x * 7 + y * 5) % 25))));

        Assert.True(Score(midTone) > Score(blownOut));
    }

    [Fact]
    public void ABlackFrameIsRejectedOutright()
    {
        using var black = Frame(160, 90, (_, _) => Grey(2));

        using var analysis = FrameExtractionService.CreateAnalysisBitmap(black);
        var quality = FrameExtractionService.AnalyzeFrame(analysis);

        // The extraction loop scores a frame this dark as zero rather than ranking it.
        Assert.True(quality.Brightness < 0.02);
    }

    /// <summary>
    /// Detail has to keep separating frames past the point the old threshold called "sharp enough",
    /// which was a Laplacian variance of 100 — a bar two thirds of real frames clear.
    /// </summary>
    [Fact]
    public void MoreDetailKeepsScoringHigher()
    {
        using var flat = Frame(160, 90, (x, y) => Grey((byte)(110 + ((x / 40) * 6))));
        using var detailed = Frame(160, 90, (x, y) => Grey((byte)(110 + (((x + y) % 2) * 60))));

        var flatScore = Score(flat);
        var detailedScore = Score(detailed);

        Assert.True(detailedScore > flatScore);
        Assert.NotEqual(Math.Round(flatScore, 3), Math.Round(detailedScore, 3));
    }

    /// <summary>
    /// Letterbox bars are not picture. Scoring them dragged every measurement toward black, which
    /// on a wide film in a 16:9 container understated brightness by about a third.
    /// </summary>
    [Fact]
    public void LetterboxBarsAreLeftOutOfTheAnalysis()
    {
        SKColor Picture(int x, int y) => Grey((byte)(120 + ((x * 3 + y * 5) % 60)));

        using var clean = Frame(160, 60, Picture);
        using var barred = Frame(160, 90, (x, y) => y < 15 || y >= 75 ? Grey(0) : Picture(x, y - 15));

        using var cleanAnalysis = FrameExtractionService.CreateAnalysisBitmap(clean);
        using var barredAnalysis = FrameExtractionService.CreateAnalysisBitmap(barred);

        var cleanBrightness = FrameExtractionService.AnalyzeFrame(cleanAnalysis).Brightness;
        var barredBrightness = FrameExtractionService.AnalyzeFrame(barredAnalysis).Brightness;

        Assert.True(Math.Abs(cleanBrightness - barredBrightness) < 0.05,
            $"bars skewed brightness: {cleanBrightness:F3} against {barredBrightness:F3}");
    }

    /// <summary>
    /// A frame with a calm strip has somewhere to put a title; one that is busy edge to edge does
    /// not, and text over it has to fight the picture.
    /// </summary>
    [Fact]
    public void AFrameWithARestfulBandHasMoreHeadroomThanABusyOne()
    {
        SKColor Busy(int x, int y) => Grey((byte)(110 + (((x + y) % 2) * 70)));

        using var busyThroughout = Frame(160, 90, Busy);
        using var calmFoot = Frame(160, 90, (x, y) => y > 64 ? Grey(115) : Busy(x, y));

        using var busyAnalysis = FrameExtractionService.CreateAnalysisBitmap(busyThroughout);
        using var calmAnalysis = FrameExtractionService.CreateAnalysisBitmap(calmFoot);

        var busy = FrameExtractionService.AnalyzeFrame(busyAnalysis).Headroom;
        var calm = FrameExtractionService.AnalyzeFrame(calmAnalysis).Headroom;

        Assert.True(calm > busy, $"calm {calm:F3} should beat busy {busy:F3}");
    }
}
