using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    private static SKColor Gray(byte v) => new(v, v, v);

    private static double Score(SKBitmap frame)
    {
        using var analysis = FrameExtractionService.CreateAnalysisBitmap(frame);
        return FrameExtractionService.CalculateQualityScore(FrameExtractionService.AnalyzeFrame(analysis));
    }

    /// <summary>
    /// A blown out frame is as useless as a crushed one, and only the crushed one was ever
    /// penalized: the old score rose with brightness and then sat at its ceiling from 5% gray up.
    /// The two frames here carry the same texture at the same amplitude and differ only in level,
    /// so nothing but the tone term can separate them.
    /// </summary>
    [Fact]
    public void ABlownOutFrameScoresBelowAMidToneOne()
    {
        byte Texture(int x, int y, int floor) => (byte)(floor + ((x * 7 + y * 5) % 40));

        using var midTone = Frame(160, 90, (x, y) => Gray(Texture(x, y, 110)));
        using var blownOut = Frame(160, 90, (x, y) => Gray(Texture(x, y, 215)));

        using var midAnalysis = FrameExtractionService.CreateAnalysisBitmap(midTone);
        using var blownAnalysis = FrameExtractionService.CreateAnalysisBitmap(blownOut);
        var mid = FrameExtractionService.AnalyzeFrame(midAnalysis);
        var blown = FrameExtractionService.AnalyzeFrame(blownAnalysis);

        // Same texture, so the detail the old score leaned on is a wash between them.
        Assert.Equal(Math.Round(mid.Sharpness, 1), Math.Round(blown.Sharpness, 1));

        Assert.True(
            FrameExtractionService.CalculateQualityScore(mid) > FrameExtractionService.CalculateQualityScore(blown),
            "a mid tone frame must outrank a blown out one");

        // The score this replaced: brightness and sharpness each against a pass mark, both long
        // since saturated, which rated these two identically.
        static double Superseded(FrameExtractionService.FrameQuality q)
            => (Math.Min(q.Brightness / 0.05, 1.0) * 0.5) + (Math.Min(q.Sharpness / 100.0, 1.0) * 0.5);

        Assert.Equal(Superseded(mid), Superseded(blown), 3);
    }

    [Fact]
    public void ABlackFrameIsRejectedOutright()
    {
        using var black = Frame(160, 90, (_, _) => Gray(2));

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
        using var flat = Frame(160, 90, (x, y) => Gray((byte)(110 + ((x / 40) * 6))));
        using var detailed = Frame(160, 90, (x, y) => Gray((byte)(110 + (((x + y) % 2) * 60))));

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
        SKColor Picture(int x, int y) => Gray((byte)(120 + ((x * 3 + y * 5) % 60)));

        using var clean = Frame(160, 60, Picture);
        using var barred = Frame(160, 90, (x, y) => y < 15 || y >= 75 ? Gray(0) : Picture(x, y - 15));

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
        SKColor Busy(int x, int y) => Gray((byte)(110 + (((x + y) % 2) * 70)));

        using var busyThroughout = Frame(160, 90, Busy);
        using var calmFoot = Frame(160, 90, (x, y) => y > 64 ? Gray(115) : Busy(x, y));

        using var busyAnalysis = FrameExtractionService.CreateAnalysisBitmap(busyThroughout);
        using var calmAnalysis = FrameExtractionService.CreateAnalysisBitmap(calmFoot);

        var busy = FrameExtractionService.AnalyzeFrame(busyAnalysis).Headroom;
        var calm = FrameExtractionService.AnalyzeFrame(calmAnalysis).Headroom;

        Assert.True(calm > busy, $"calm {calm:F3} should beat busy {busy:F3}");
    }

    /// <summary>
    /// A sampled frame is decoded only as large as scoring needs: a 4K or 1080p JPEG comes back at
    /// twice the analysis size, and one already smaller than that is left whole.
    /// </summary>
    [Theory]
    [InlineData(3840, 2160, 960)]
    [InlineData(1920, 1080, 960)]
    [InlineData(640, 360, 640)]
    public void DecodeForAnalysis_DecodesOnlyAsLargeAsScoringNeeds(int width, int height, int expectedWidth)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ag-decode-{Guid.NewGuid():N}.jpg");
        try
        {
            using (var frame = Frame(width, height, (x, y) => Gray((byte)((x ^ y) & 0xFF))))
            using (var image = SKImage.FromBitmap(frame))
            using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 85))
            using (var file = File.Create(path))
            {
                data.SaveTo(file);
            }

            using var decoded = FrameExtractionService.DecodeForAnalysis(path);

            Assert.NotNull(decoded);
            Assert.Equal(expectedWidth, decoded!.Width);
            Assert.Equal(expectedWidth * height / width, decoded.Height);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Samples finish in any order once they run side by side. Which ones are kept must not depend
    /// on that: the best scores win, and a tie goes to the earlier attempt.
    /// </summary>
    [Fact]
    public void TryAddCandidate_KeepsTheSameSetWhateverOrderAttemptsFinishIn()
    {
        var attempts = new[] { 0.4, 0.9, 0.4, 0.7, 0.1, 0.9, 0.4 }
            .Select((score, attempt) => new FrameExtractionService.FrameCandidate($"/missing/{attempt}.jpg", score, attempt))
            .ToArray();

        string Kept(IEnumerable<FrameExtractionService.FrameCandidate> order)
        {
            var kept = new List<FrameExtractionService.FrameCandidate>();
            foreach (var candidate in order)
            {
                FrameExtractionService.TryAddCandidate(kept, 4, candidate);
            }

            return string.Join(",", kept.Select(c => c.Attempt).OrderBy(a => a));
        }

        var expected = Kept(attempts);
        Assert.Equal("0,1,3,5", expected);
        Assert.Equal(expected, Kept(attempts.Reverse()));
        Assert.Equal(expected, Kept(attempts.OrderBy(c => c.Path.GetHashCode(StringComparison.Ordinal))));
    }
}
