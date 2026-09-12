using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// A grid canvas tiles several frames into one picture, the way a wall of photographs is hung.
/// </summary>
public class GridComposerTests
{
    private static SKBitmap Frame(SKColor color)
    {
        var bitmap = new SKBitmap(40, 40, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.Erase(color);
        return bitmap;
    }

    private static List<SKBitmap> Frames(int count) =>
        Enumerable.Range(0, count).Select(i => Frame(new SKColor((byte)(40 + i * 20), 90, 160))).ToList();

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(9)]
    public void EveryFrameIsDrawn(int count)
    {
        var frames = Frames(count);
        try
        {
            using var grid = GridComposer.Compose(frames, 400, 400, 4f);

            // Each frame is a distinct colour, so finding all of them proves none was left out.
            var pixels = grid.Pixels;
            foreach (var frame in frames)
            {
                var wanted = frame.GetPixel(0, 0);
                Assert.Contains(pixels, p => p.Red == wanted.Red && p.Green == wanted.Green && p.Blue == wanted.Blue);
            }
        }
        finally
        {
            frames.ForEach(f => f.Dispose());
        }
    }

    /// <summary>
    /// The cells share the space evenly, so a grid reads as a grid rather than as one large frame
    /// beside some small ones.
    /// </summary>
    [Fact]
    public void TheCellsAreEvenlySized()
    {
        var frames = Frames(4);
        try
        {
            using var grid = GridComposer.Compose(frames, 400, 400, 0f);

            // Four frames make two columns of two, so the seam sits exactly halfway.
            var topLeft = grid.GetPixel(100, 100);
            var topRight = grid.GetPixel(300, 100);
            var bottomLeft = grid.GetPixel(100, 300);
            var bottomRight = grid.GetPixel(300, 300);

            Assert.Equal(4, new[] { topLeft, topRight, bottomLeft, bottomRight }.Distinct().Count());
        }
        finally
        {
            frames.ForEach(f => f.Dispose());
        }
    }

    [Fact]
    public void TheGapShowsBetweenTheFrames()
    {
        var frames = Frames(4);
        try
        {
            using var grid = GridComposer.Compose(frames, 400, 400, 20f);

            // Dead centre falls in the gap where the four cells meet.
            Assert.Equal(SKColors.Black, grid.GetPixel(200, 200));
        }
        finally
        {
            frames.ForEach(f => f.Dispose());
        }
    }

    [Fact]
    public void TheCountIsBounded()
    {
        Assert.Equal(2, GridComposer.Clamp(0));
        Assert.Equal(2, GridComposer.Clamp(1));
        Assert.Equal(16, GridComposer.Clamp(500));
        Assert.Equal(6, GridComposer.Clamp(6));
    }

    [Fact]
    public void NoFramesLeavesAnEmptyCanvasRatherThanThrowing()
    {
        using var grid = GridComposer.Compose(Array.Empty<SKBitmap>(), 100, 100, 4f);

        Assert.Equal(100, grid.Width);
    }
}
