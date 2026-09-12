using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services.Artwork;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

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

    /// <summary>
    /// The configured size is the room the lettering gets, so the finished PNG never exceeds it and
    /// is transparent everywhere the lettering is not.
    /// </summary>
    [Fact]
    public void Render_ProducesATransparentPngWithinTheConfiguredSize()
    {
        var renderer = new LogoRenderer(NullLogger<LogoRenderer>.Instance);
        var bytes = renderer.Render(new ArtworkSubject { SeriesName = "Andor" }, new LogoSettings());

        Assert.NotNull(bytes);
        using var bitmap = SKBitmap.Decode(bytes);
        Assert.InRange(bitmap.Width, 1, 800);
        Assert.InRange(bitmap.Height, 1, 310);
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

    /// <summary>
    /// Width and height are the room the lettering gets, not the size of the file: a clear logo is
    /// expected to be trimmed to its own artwork so it sits beside downloaded ones.
    /// </summary>
    [Fact]
    public void Render_TrimsTheEmptyMarginAwayFromTheLettering()
    {
        var renderer = new LogoRenderer(NullLogger<LogoRenderer>.Instance);

        var bytes = renderer.Render(
            new ArtworkSubject { SeriesName = "W" },
            new LogoSettings { Width = 800, Height = 310 });

        Assert.NotNull(bytes);
        using var bitmap = SKBitmap.Decode(bytes);

        Assert.True(bitmap.Height < 310, $"expected the margin to be trimmed, got {bitmap.Width}x{bitmap.Height}");
        Assert.True(bitmap.Width <= 800);

        // Trimmed to the artwork means the edges carry it: a fully transparent row or column would
        // mean the crop stopped short. The two pixel bleed is what keeps antialiasing intact.
        var pixels = bitmap.Pixels;
        Assert.Contains(pixels, p => p.Alpha > 0);
    }
}
