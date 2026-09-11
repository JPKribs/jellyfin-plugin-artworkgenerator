using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Services.Artwork;
using Jellyfin.Plugin.EpisodePosterGenerator.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Tests;

/// <summary>
/// Tests for generated logo layout and output.
/// </summary>
public class LogoRendererTests
{
    [Fact]
    public void ChooseLayout_KeepsAShortNameOnOneLine()
    {
        var lines = LogoRenderer.ChooseLayout("Andor", SKTypeface.Default, 752f, 266f, 2, out var size);

        Assert.Single(lines);
        Assert.True(size > 0f);
    }

    /// <summary>
    /// A long name on one line would be tiny; splitting it lets the text grow.
    /// </summary>
    [Fact]
    public void ChooseLayout_SplitsALongNameWhenThatGrowsTheText()
    {
        var lines = LogoRenderer.ChooseLayout("The Extraordinary Misadventures of Someone Else", SKTypeface.Default, 752f, 266f, 2, out _);

        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void ChooseLayout_HonoursASingleLineLimit()
    {
        var lines = LogoRenderer.ChooseLayout("The Extraordinary Misadventures of Someone Else", SKTypeface.Default, 752f, 266f, 1, out _);

        Assert.Single(lines);
    }

    [Fact]
    public void Render_ProducesATransparentPngOfTheConfiguredSize()
    {
        var renderer = new LogoRenderer(NullLogger<LogoRenderer>.Instance);
        var bytes = renderer.Render(new ArtworkSubject { SeriesName = "Andor" }, new LogoSettings());

        Assert.NotNull(bytes);
        using var bitmap = SKBitmap.Decode(bytes);
        Assert.Equal(800, bitmap.Width);
        Assert.Equal(310, bitmap.Height);
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
    }

    [Fact]
    public void Layout_PutsASmallTitleAboveALargeSubtitle()
    {
        var lines = LogoRenderer.Layout(new LogoLines("Andor", "Star Wars", true), SKTypeface.Default, 752f, 266f, 2, 0.45f);

        Assert.Equal(2, lines.Count);
        Assert.Equal("Star Wars", lines[0].Text);
        Assert.Equal("Andor", lines[1].Text);
        Assert.True(lines[0].Size < lines[1].Size);
    }

    [Fact]
    public void Layout_PutsASmallSubtitleBelowALargeTitle()
    {
        var lines = LogoRenderer.Layout(new LogoLines("Star Wars", "Andor", false), SKTypeface.Default, 752f, 266f, 2, 0.45f);

        Assert.Equal("Star Wars", lines[0].Text);
        Assert.Equal("Andor", lines[^1].Text);
        Assert.True(lines[^1].Size < lines[0].Size);
    }

    /// <summary>
    /// A photo fill shows the photo through the letters and leaves the rest transparent.
    /// </summary>
    [Fact]
    public void Render_PhotoFill_ShowsThePhotoInsideTheLetters()
    {
        using var photo = new SKBitmap(64, 64);
        photo.Erase(SKColors.Red);
        var renderer = new LogoRenderer(NullLogger<LogoRenderer>.Instance);

        var bytes = renderer.Render(new ArtworkSubject { SeriesName = "WWW" }, new LogoSettings { Fill = LogoFill.Photo }, photo);

        Assert.NotNull(bytes);
        using var bitmap = SKBitmap.Decode(bytes);
        var pixels = bitmap.Pixels;
        Assert.Contains(pixels, p => p.Alpha == 255 && p.Red > 200 && p.Green < 40 && p.Blue < 40);
        Assert.DoesNotContain(pixels, p => p.Alpha == 255 && p.Green > 200);
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
    }
}
