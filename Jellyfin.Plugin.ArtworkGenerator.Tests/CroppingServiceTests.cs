using System;
using System.Globalization;
using System.Threading;
using Jellyfin.Plugin.ArtworkGenerator.Services;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Tests for aspect ratio parsing in <see cref="CroppingService"/>. The ratio is free text in
/// the configuration page, so it has to parse identically regardless of the server's locale.
/// </summary>
public class CroppingServiceTests
{
    [Theory]
    [InlineData("16:9", 16f / 9f)]
    [InlineData("4:3", 4f / 3f)]
    [InlineData("1:1", 1f)]
    [InlineData("2.35:1", 2.35f)]
    [InlineData("1.85:1", 1.85f)]
    public void ParseAspectRatio_ParsesValidRatios(string ratio, float expected)
    {
        Assert.Equal(expected, CroppingService.ParseAspectRatio(ratio), 4);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("16")]
    [InlineData("16:9:3")]
    [InlineData("wide:tall")]
    [InlineData("16:0")]
    [InlineData("0:9")]
    [InlineData("-16:9")]
    public void ParseAspectRatio_FallsBackToSixteenNineOnGarbage(string ratio)
    {
        Assert.Equal(16f / 9f, CroppingService.ParseAspectRatio(ratio), 4);
    }

    /// <summary>
    /// Under a comma-decimal locale, current-culture parsing reads "2.35" as 235, producing a
    /// 235:1 crop. Parsing must be culture-invariant.
    /// </summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("pt-BR")]
    public void ParseAspectRatio_IsCultureInvariant(string cultureName)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            Thread.CurrentThread.CurrentCulture = new CultureInfo(cultureName);

            Assert.Equal(2.35f, CroppingService.ParseAspectRatio("2.35:1"), 4);
            Assert.Equal(16f / 9f, CroppingService.ParseAspectRatio("16:9"), 4);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            Thread.CurrentThread.CurrentCulture = original;
        }
    }


    /// <summary>
    /// A poster cropped down by letterbox removal is brought back up to a usable short side, so it
    /// does not sit beside a full size one in the picker at half the resolution.
    /// </summary>
    [Fact]
    public void NormalizedSize_ScalesACroppedPosterUpToTheMinimumShortSide()
    {
        var (width, height) = CroppingService.NormalizedSize(533, 800);

        Assert.Equal(720, Math.Min(width, height));

        // The shape is kept: 533x800 is 2:3, and so is the result.
        Assert.InRange((float)width / height, (2f / 3f) - 0.01f, (2f / 3f) + 0.01f);
    }

    /// <summary>A poster already at a good size is left exactly as it is.</summary>
    [Theory]
    [InlineData(720, 1080)]
    [InlineData(1920, 1080)]
    [InlineData(1000, 1500)]
    public void NormalizedSize_LeavesAdequatePostersAlone(int width, int height)
    {
        Assert.Equal((width, height), CroppingService.NormalizedSize(width, height));
    }

    /// <summary>
    /// A very small frame is not blown up without limit: past the cap the result would be soft
    /// rather than detailed.
    /// </summary>
    [Fact]
    public void NormalizedSize_StopsAtTheUpscaleCap()
    {
        var (width, height) = CroppingService.NormalizedSize(100, 150);

        Assert.Equal((200, 300), (width, height));
        Assert.True(Math.Min(width, height) < 720, "The cap is expected to win over the target here.");
    }

    /// <summary>A degenerate size is returned untouched rather than throwing or dividing by zero.</summary>
    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-10, 10)]
    public void NormalizedSize_IgnoresDegenerateSizes(int width, int height)
    {
        Assert.Equal((width, height), CroppingService.NormalizedSize(width, height));
    }


    // Letterboxed
    // A frame with black bars top and bottom, like a scope film in a 16:9 container.
    private static SKBitmap Letterboxed(int width, int height, int barHeight)
    {
        var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Black);
            using var paint = new SKPaint { Color = new SKColor(180, 140, 90) };
            canvas.DrawRect(new SKRect(0, barHeight, width, height - barHeight), paint);
        }

        return bitmap;
    }

    /// <summary>
    /// The whole crop path, not just the arithmetic: a letterboxed frame cropped to a portrait
    /// poster comes out at a usable size rather than at whatever few pixels survived the bars.
    /// This is the case that put a 503x755 poster next to a 720x1080 one in the image picker.
    /// </summary>
    [Fact]
    public void CropPoster_ScalesAHeavilyLetterboxedPortraitPosterBackUp()
    {
        var service = new CroppingService(NullLogger<CroppingService>.Instance);
        using var source = Letterboxed(1920, 1080, 140);
        var settings = new PosterSettings
        {
            EnableLetterboxDetection = true,
            PosterFill = PosterFill.Fit,
            PosterDimensionRatio = "2:3"
        };

        using var result = service.CropPoster(source, settings);

        Assert.Equal(720, Math.Min(result.Width, result.Height));
        Assert.InRange((float)result.Width / result.Height, (2f / 3f) - 0.01f, (2f / 3f) + 0.01f);
    }

    /// <summary>
    /// Original means the frame exactly as it came, so it is never scaled up even when small.
    /// </summary>
    [Fact]
    public void CropPoster_LeavesOriginalFillAtItsOwnSize()
    {
        var service = new CroppingService(NullLogger<CroppingService>.Instance);
        using var source = Letterboxed(1920, 1080, 140);
        var settings = new PosterSettings
        {
            EnableLetterboxDetection = true,
            PosterFill = PosterFill.Original,
            PosterDimensionRatio = "2:3"
        };

        using var result = service.CropPoster(source, settings);

        // The bars are gone, but nothing has been magnified.
        Assert.Equal(1920, result.Width);
        Assert.Equal(800, result.Height);
    }
}
