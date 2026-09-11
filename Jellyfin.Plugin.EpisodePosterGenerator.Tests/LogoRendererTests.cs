using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Services.Artwork;
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
}
