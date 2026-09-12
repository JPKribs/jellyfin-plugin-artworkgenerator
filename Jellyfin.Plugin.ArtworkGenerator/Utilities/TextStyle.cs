using System;
using System.Collections.Generic;
using SkiaSharp;

namespace Jellyfin.Plugin.ArtworkGenerator.Utilities
{
    /// <summary>
    /// A font together with the paints used to draw it: the fill, and an optional blurred
    /// shadow drawn one offset below and to the right.
    /// </summary>
    /// <remarks>
    /// SkiaSharp 3 moved text out of <see cref="SKPaint"/> and into <see cref="SKFont"/>, so
    /// drawing a line of text now takes a font, a paint, and an alignment. Bundling them keeps
    /// every style measuring and drawing with the same objects, and exposes the font's real
    /// ascent and descent so line boxes come from metrics rather than from the em size.
    /// </remarks>
    public sealed class TextStyle : IDisposable
    {
        private readonly SKMaskFilter? _shadowBlur;
        private bool _disposed;

        public TextStyle(SKFont font, SKPaint fill, SKPaint? shadow, SKMaskFilter? shadowBlur, SKTextAlign align, float shadowOffset)
        {
            ArgumentNullException.ThrowIfNull(font);
            ArgumentNullException.ThrowIfNull(fill);

            Font = font;
            Fill = fill;
            Shadow = shadow;
            _shadowBlur = shadowBlur;
            Align = align;
            ShadowOffset = shadowOffset;
            Metrics = font.Metrics;
        }

        public SKFont Font { get; }

        public SKPaint Fill { get; }

        public SKPaint? Shadow { get; }

        public SKTextAlign Align { get; }

        public float ShadowOffset { get; }

        public SKFontMetrics Metrics { get; }

        public float Size => Font.Size;

        /// <summary>Gets the distance from the baseline to the top of the tallest glyph, as a positive number.</summary>
        public float Ascent => -Metrics.Ascent;

        /// <summary>Gets the distance from the baseline to the bottom of the deepest descender.</summary>
        public float Descent => Metrics.Descent;

        /// <summary>Gets the height one line of glyphs really occupies, ascent plus descent.</summary>
        public float LineBox => Ascent + Descent;

        /// <summary>Gets the baseline-to-baseline distance between wrapped lines.</summary>
        public float LineHeight => Size * RenderConstants.LineHeightMultiplier;

        // BlockHeight
        // The height a run of lines occupies: one full line box plus the leading for each line
        // after the first. Reserving this rather than a multiple of the em size is what keeps
        // descenders inside the slot instead of hanging past the safe area.
        public float BlockHeight(int lineCount)
        {
            return LineBox + ((Math.Max(1, lineCount) - 1) * LineHeight);
        }

        public float MeasureWidth(string text) => Font.MeasureText(text);

        // MeasureBounds
        // The tight glyph bounding box relative to the baseline origin.
        public SKRect MeasureBounds(string text)
        {
            Font.MeasureText(text, out SKRect bounds);
            return bounds;
        }

        // FirstBaselineCentered
        // The baseline of the first line when a run of lines is centered vertically in a slot.
        // A slot taller than the run splits the slack evenly above and below.
        public float FirstBaselineCentered(SKRect slot, int lineCount)
        {
            return slot.Top + Math.Max(0f, (slot.Height - BlockHeight(lineCount)) / 2f) + Ascent;
        }

        public float BaselineAtTop(SKRect slot) => slot.Top + Ascent;

        public float BaselineAtBottom(SKRect slot) => slot.Bottom - Descent;

        // Draw
        // Draws one line of text at the baseline, shadow first when the style has one.
        public void Draw(SKCanvas canvas, string text, float x, float baseline)
        {
            ArgumentNullException.ThrowIfNull(canvas);

            if (Shadow != null)
            {
                canvas.DrawText(text, x + ShadowOffset, baseline + ShadowOffset, Align, Font, Shadow);
            }

            canvas.DrawText(text, x, baseline, Align, Font, Fill);
        }

        // DrawLines
        // Draws consecutive lines one line height apart, starting at the given baseline.
        public void DrawLines(SKCanvas canvas, IReadOnlyList<string> lines, float x, float firstBaseline)
        {
            ArgumentNullException.ThrowIfNull(lines);

            for (int i = 0; i < lines.Count; i++)
            {
                Draw(canvas, lines[i], x, firstBaseline + (i * LineHeight));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Fill.Dispose();
            Shadow?.Dispose();
            _shadowBlur?.Dispose();
            Font.Dispose();
        }
    }
}
