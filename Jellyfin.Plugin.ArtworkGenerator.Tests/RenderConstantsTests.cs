using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Tests for the poster-scaled render sizes and the text style built on them.
/// </summary>
public class RenderConstantsTests
{
    /// <summary>
    /// A shadow or rule fixed in pixels was a hairline at 4K and a slab on a DVD rip; sizes
    /// scale linearly with poster height from the 1080 reference.
    /// </summary>
    [Fact]
    public void Scaled_GrowsLinearlyWithPosterHeight()
    {
        Assert.Equal(2f, RenderConstants.Scaled(2f, 1080f), 3);
        Assert.Equal(4f, RenderConstants.Scaled(2f, 2160f), 3);
        Assert.Equal(RenderConstants.ShadowOffset(2160f), RenderConstants.ShadowOffset(1080f) * 2f, 3);
        Assert.Equal(RenderConstants.SeparatorStrokeWidth(2160f), RenderConstants.SeparatorStrokeWidth(1080f) * 2f, 3);
    }

    [Fact]
    public void Scaled_NeverDropsBelowOnePixel()
    {
        Assert.Equal(1f, RenderConstants.Scaled(2f, 100f), 3);
        Assert.Equal(1f, RenderConstants.Scaled(2f, 0f), 3);
        Assert.True(RenderConstants.ShadowBlurSigma(240f) >= 1f);
    }

    /// <summary>
    /// The block a run of lines reserves comes from the font's real ascent and descent, so a
    /// descender on the last line stays inside the slot instead of hanging past the safe area.
    /// </summary>
    [Fact]
    public void TextStyle_BlockHeightUsesFontMetrics()
    {
        using var style = PaintFactory.CreateTextStyle(SKColors.White, 40f, SKTypeface.Default, 1080f);

        Assert.True(style.Ascent > 0f);
        Assert.True(style.Descent >= 0f);
        Assert.Equal(style.Ascent + style.Descent, style.BlockHeight(1), 3);
        Assert.Equal(style.BlockHeight(1) + style.LineHeight, style.BlockHeight(2), 3);
        Assert.Equal(style.BlockHeight(1), style.BlockHeight(0), 3);
    }

    [Fact]
    public void TextStyle_CentersARunInsideItsSlot()
    {
        using var style = PaintFactory.CreateTextStyle(SKColors.White, 40f, SKTypeface.Default, 1080f);
        var slot = SKRect.Create(0, 100, 500, style.BlockHeight(2));

        // A full two line run fills the slot: first baseline is one ascent below the top.
        Assert.Equal(slot.Top + style.Ascent, style.FirstBaselineCentered(slot, 2), 3);

        // A one line run leaves one line height of slack, split evenly above and below.
        Assert.Equal(slot.Top + (style.LineHeight / 2f) + style.Ascent, style.FirstBaselineCentered(slot, 1), 3);
    }

    [Fact]
    public void TextStyle_ShadowScalesWithThePoster()
    {
        using var small = PaintFactory.CreateTextStyle(SKColors.White, 40f, SKTypeface.Default, 1080f);
        using var large = PaintFactory.CreateTextStyle(SKColors.White, 40f, SKTypeface.Default, 2160f);
        using var plain = PaintFactory.CreateTextStyle(SKColors.White, 40f, SKTypeface.Default, 1080f, withShadow: false);

        Assert.NotNull(small.Shadow);
        Assert.Equal(small.ShadowOffset * 2f, large.ShadowOffset, 3);
        Assert.Null(plain.Shadow);
        Assert.Equal(0f, plain.ShadowOffset);
    }
}
