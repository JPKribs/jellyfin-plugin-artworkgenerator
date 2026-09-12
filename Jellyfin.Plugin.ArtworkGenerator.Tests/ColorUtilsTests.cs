using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Tests for the hex color parsing in <see cref="ColorUtils"/>.
/// </summary>
public class ColorUtilsTests
{
    [Theory]
    [InlineData("#FFFFFFFF", 255, 255, 255, 255)]   // ARGB, fully opaque white
    [InlineData("#66000000", 0, 0, 0, 0x66)]         // ARGB, semi transparent black
    [InlineData("#FF112233", 0x11, 0x22, 0x33, 0xFF)] // ARGB, alpha first then RGB
    [InlineData("#FF0000", 255, 0, 0, 255)]          // RGB, alpha defaults to opaque
    public void ParseHexColor_ParsesArgbAndRgb(string hex, byte r, byte g, byte b, byte a)
    {
        var color = ColorUtils.ParseHexColor(hex);
        Assert.Equal(r, color.Red);
        Assert.Equal(g, color.Green);
        Assert.Equal(b, color.Blue);
        Assert.Equal(a, color.Alpha);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#abc")]   // not 6 or 8 hex digits
    public void ParseHexColor_InvalidOrEmpty_ReturnsWhite(string hex)
    {
        Assert.Equal(SKColors.White, ColorUtils.ParseHexColor(hex));
    }
}
