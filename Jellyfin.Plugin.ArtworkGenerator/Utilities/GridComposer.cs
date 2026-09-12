using System;
using System.Collections.Generic;
using SkiaSharp;

namespace Jellyfin.Plugin.ArtworkGenerator.Utilities;

/// <summary>
/// Tiles several frames into one canvas, the way a wall of photographs is hung.
/// </summary>
public static class GridComposer
{
    /// <summary>
    /// Lays the frames out in as square a grid as their number allows, every cell the same size,
    /// with a gap between them. Each frame is cropped to fill its cell rather than squashed, and a
    /// short last row is centered so it reads as a choice rather than a gap.
    /// </summary>
    public static SKBitmap Compose(IReadOnlyList<SKBitmap> frames, int width, int height, float gap)
    {
        ArgumentNullException.ThrowIfNull(frames);

        var grid = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(grid);
        // Black rather than transparent, so the gap is a matte the design chose rather than
        // whatever the encoder happens to make of an empty pixel.
        canvas.Clear(SKColors.Black);

        if (frames.Count == 0)
        {
            return grid;
        }

        var columns = (int)Math.Ceiling(Math.Sqrt(frames.Count));
        var rows = (int)Math.Ceiling(frames.Count / (double)columns);
        gap = Math.Clamp(gap, 0, Math.Min(width, height) / 4f);

        var cellWidth = (width - (gap * (columns - 1))) / columns;
        var cellHeight = (height - (gap * (rows - 1))) / rows;

        using var paint = new SKPaint { IsAntialias = true };

        for (var i = 0; i < frames.Count; i++)
        {
            var row = i / columns;
            var column = i % columns;

            // The last row may hold fewer than a full row, so it is centered under the rest.
            var inRow = Math.Min(columns, frames.Count - (row * columns));
            var indent = (columns - inRow) * (cellWidth + gap) / 2f;

            var cell = SKRect.Create(
                indent + (column * (cellWidth + gap)),
                row * (cellHeight + gap),
                cellWidth,
                cellHeight);

            PaintFactory.DrawBitmapCover(canvas, frames[i], cell, paint);
        }

        return grid;
    }

    /// <summary>
    /// How many frames a grid of this many cells should ask for, bounded so a design cannot ask for
    /// more extractions than a poster could ever show.
    /// </summary>
    public static int Clamp(int count) => Math.Clamp(count, 2, 16);
}
