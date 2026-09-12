using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Jellyfin.Plugin.ArtworkGenerator.Services.Posters;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Text position is offered by every design whose text is a block laid over the image, and withheld
/// by the ones that make the text part of the artwork. These tests hold that line, and hold the one
/// sentence each design uses to say what its title and subtitle are.
/// </summary>
public class TextPositionTests
{
    /// <summary>
    /// The designs that cannot honour a position: the lettering is cut out of the image, rides a
    /// tilted sash, runs sideways up an edge, or is already placed by the frame's own edge control.
    /// </summary>
    private static readonly PosterStyle[] CannotBePositioned =
    {
        PosterStyle.Cutout,
        PosterStyle.Striped,
        PosterStyle.Fade,
        PosterStyle.Frame
    };

    [Fact]
    public void EveryOtherDesignOffersTextPosition()
    {
        foreach (var generator in PreviewService.GetStyleCatalog())
        {
            var offered = !generator.SettingRules.TryGetValue(PosterSettingRules.TextPosition, out var state)
                || state == PosterSettingState.Optional;

            Assert.Equal(!CannotBePositioned.Contains(generator.Style), offered);
        }
    }

    /// <summary>
    /// A design that hides the setting must not be reading it either, or the page would be hiding a
    /// control that still changes the image.
    /// </summary>
    [Fact]
    public void HiddenDesignsAreHiddenDeliberately()
    {
        foreach (var style in CannotBePositioned)
        {
            var generator = PreviewService.GetStyleCatalog().Single(g => g.Style == style);

            Assert.Equal(
                PosterSettingState.Hidden,
                generator.SettingRules[PosterSettingRules.TextPosition]);
        }
    }

    /// <summary>
    /// The default has to mean "leave it exactly where this design has always put it", which is the
    /// only reason adding this setting moved nothing.
    /// </summary>
    [Fact]
    public void DefaultIsAuto()
    {
        Assert.Equal(TextPosition.Auto, new PosterSettings().TextPosition);
        Assert.Equal(TextAlignment.Auto, new PosterSettings().TextAlignment);
    }

    /// <summary>
    /// The designs that cannot pull their text to a side: it rides a tilted sash, runs sideways up
    /// an edge, or is already placed along a border by the frame's own control. Cutout is not among
    /// them — its lettering is fixed, but the title line under it is ordinary text.
    /// </summary>
    private static readonly PosterStyle[] CannotBeAligned =
    {
        PosterStyle.Striped,
        PosterStyle.Fade,
        PosterStyle.Frame
    };

    [Fact]
    public void EveryOtherDesignOffersTextAlignment()
    {
        foreach (var generator in PreviewService.GetStyleCatalog())
        {
            var offered = !generator.SettingRules.TryGetValue(PosterSettingRules.TextAlignment, out var state)
                || state == PosterSettingState.Optional;

            Assert.Equal(!CannotBeAligned.Contains(generator.Style), offered);
        }
    }

    [Theory]
    [InlineData(PosterStyle.Standard)]
    [InlineData(PosterStyle.Bloom)]
    [InlineData(PosterStyle.FrostedGlass)]
    [InlineData(PosterStyle.Brush)]
    [InlineData(PosterStyle.Logo)]
    public void OfferedDesigns_DrawADifferentImageAtEachAlignment(PosterStyle style)
    {
        var left = RenderAligned(style, TextAlignment.Left);
        var center = RenderAligned(style, TextAlignment.Center);
        var right = RenderAligned(style, TextAlignment.Right);

        Assert.False(left.SequenceEqual(center));
        Assert.False(center.SequenceEqual(right));
        Assert.False(left.SequenceEqual(right));
    }

    /// <summary>
    /// Auto has to land on the design's own side, which is what let this be added without moving a
    /// single existing image.
    /// </summary>
    [Theory]
    [InlineData(PosterStyle.Standard, TextAlignment.Center)]
    [InlineData(PosterStyle.Brush, TextAlignment.Left)]
    [InlineData(PosterStyle.Timeline, TextAlignment.Left)]
    public void AutoMatchesTheDesignsOwnSide(PosterStyle style, TextAlignment natural)
    {
        Assert.True(RenderAligned(style, TextAlignment.Auto).SequenceEqual(RenderAligned(style, natural)));
    }

    [Theory]
    [InlineData(PosterStyle.Striped)]
    [InlineData(PosterStyle.Fade)]
    [InlineData(PosterStyle.Frame)]
    public void HiddenDesigns_IgnoreTheAlignmentEntirely(PosterStyle style)
    {
        var auto = RenderAligned(style, TextAlignment.Auto);

        Assert.True(auto.SequenceEqual(RenderAligned(style, TextAlignment.Left)));
        Assert.True(auto.SequenceEqual(RenderAligned(style, TextAlignment.Right)));
    }

    private static byte[] RenderAligned(PosterStyle style, TextAlignment alignment)
    {
        var assetRoot = Path.Combine(Path.GetTempPath(), "ag-textposition-tests");
        var preview = new Services.Posters.PreviewService(NullLoggerFactory.Instance, assetRoot);

        var bytes = preview.GeneratePreview(
            new PosterSettings { PosterStyle = style, TextAlignment = alignment },
            ArtworkItemKind.Episode);

        Assert.NotNull(bytes);
        return bytes!;
    }

    [Fact]
    public void EveryDesignSaysWhatItsTitleAndSubtitleAre()
    {
        foreach (var generator in PreviewService.GetStyleCatalog())
        {
            foreach (var sentence in new[] { generator.PrimaryDescription, generator.SecondaryDescription })
            {
                Assert.False(string.IsNullOrWhiteSpace(sentence));
                Assert.EndsWith(".", sentence, System.StringComparison.Ordinal);

                // One sentence, as the sections have room for exactly one line.
                Assert.Equal(1, sentence.Count(c => c == '.'));
            }
        }
    }

    private static byte[] Render(PosterStyle style, TextPosition position)
    {
        var assetRoot = Path.Combine(Path.GetTempPath(), "ag-textposition-tests");
        var preview = new Services.Posters.PreviewService(NullLoggerFactory.Instance, assetRoot);

        var bytes = preview.GeneratePreview(
            new PosterSettings { PosterStyle = style, TextPosition = position },
            ArtworkItemKind.Episode);

        Assert.NotNull(bytes);
        return bytes!;
    }

    /// <summary>
    /// The whole point of the setting: on a design that offers it, each choice has to actually move
    /// the text, and the default has to land on the design's own placement.
    /// </summary>
    [Theory]
    [InlineData(PosterStyle.Standard)]
    [InlineData(PosterStyle.Bloom)]
    [InlineData(PosterStyle.FrostedGlass)]
    [InlineData(PosterStyle.Brush)]
    [InlineData(PosterStyle.Timeline)]
    public void OfferedDesigns_DrawADifferentImageAtEachPosition(PosterStyle style)
    {
        var top = Render(style, TextPosition.Top);
        var center = Render(style, TextPosition.Center);
        var bottom = Render(style, TextPosition.Bottom);

        Assert.False(top.SequenceEqual(center));
        Assert.False(center.SequenceEqual(bottom));
        Assert.False(top.SequenceEqual(bottom));
    }

    /// <summary>
    /// Auto has to resolve to the design's own placement, which is what let this setting be added
    /// without moving a single existing image.
    /// </summary>
    [Theory]
    [InlineData(PosterStyle.Standard, TextPosition.Bottom)]
    [InlineData(PosterStyle.Bloom, TextPosition.Center)]
    public void AutoMatchesTheDesignsOwnPlacement(PosterStyle style, TextPosition natural)
    {
        Assert.True(Render(style, TextPosition.Auto).SequenceEqual(Render(style, natural)));
    }

    /// <summary>
    /// A design that hides the setting must ignore it too, or the page would be withholding a
    /// control that still changes the image.
    /// </summary>
    [Theory]
    [InlineData(PosterStyle.Cutout)]
    [InlineData(PosterStyle.Striped)]
    [InlineData(PosterStyle.Fade)]
    [InlineData(PosterStyle.Frame)]
    public void HiddenDesigns_IgnoreThePositionEntirely(PosterStyle style)
    {
        var auto = Render(style, TextPosition.Auto);

        Assert.True(auto.SequenceEqual(Render(style, TextPosition.Top)));
        Assert.True(auto.SequenceEqual(Render(style, TextPosition.Center)));
    }
}
