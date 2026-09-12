using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using SkiaSharp;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Posters
{
    /// <summary>
    /// The overlay half of every design, kept beside the pipeline rather than inside it.
    /// </summary>
    public abstract partial class BasePosterGenerator
    {
        // RenderOverlay
        // Applies a color overlay with optional gradient to the poster.
        protected virtual void RenderOverlay(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            if (!TryGetOverlayColor(settings, out var primaryColor))
            {
                return;
            }

            FillOverlay(skCanvas, settings, SKRect.Create(width, height), primaryColor);
        }

        // TryGetOverlayColor
        // The guard every overlay shares: an unset or fully transparent overlay color means the
        // style draws no overlay at all.
        protected static bool TryGetOverlayColor(PosterSettings settings, out SKColor color)
        {
            color = SKColors.Empty;

            if (settings == null || string.IsNullOrEmpty(settings.OverlayColor))
            {
                return false;
            }

            color = ColorUtils.ParseHexColor(settings.OverlayColor);
            return color.Alpha != 0;
        }

        // FillOverlay
        // Paints the overlay across a rectangle, flat or as the configured gradient. Every style
        // that lays down an overlay goes through here, so a style that punches a shape out of one
        // gets the same gradient support as a style that does not.
        protected void FillOverlay(SKCanvas canvas, PosterSettings settings, SKRect rect, SKColor primaryColor)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentNullException.ThrowIfNull(settings);

            if (settings.OverlayGradient == OverlayGradient.None)
            {
                using var flatPaint = new SKPaint
                {
                    Color = primaryColor,
                    Style = SKPaintStyle.Fill
                };
                canvas.DrawRect(rect, flatPaint);
                return;
            }

            var secondaryColor = ColorUtils.ParseHexColor(settings.OverlaySecondaryColor);
            if (secondaryColor.Alpha == 0)
            {
                secondaryColor = primaryColor;
            }

            // SKPaint does not own its shader, so the gradient is disposed here rather than left to
            // the finalizer — a full library run creates one per poster.
            using var gradient = CreateOverlayGradient(settings.OverlayGradient, rect, primaryColor, secondaryColor);
            if (gradient == null)
            {
                return;
            }

            using var gradientPaint = new SKPaint
            {
                Shader = gradient,
                Style = SKPaintStyle.Fill,
                IsDither = true
            };
            canvas.DrawRect(rect, gradientPaint);
        }

        // DrawPunchedOverlay
        // The shape Cutout and Brush share: lay the overlay into its own layer, erase something out
        // of it so the image shows through the hole, then optionally trace the hole's edge. The
        // outline has to be drawn after the layer is restored in some styles and before the punch
        // in others, which is why both moments are offered rather than one.
        protected void DrawPunchedOverlay(
            SKCanvas canvas,
            PosterSettings settings,
            int width,
            int height,
            Action<SKCanvas, SKColor> punch,
            Action<SKCanvas, SKColor>? afterRestore = null)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentNullException.ThrowIfNull(punch);

            if (!TryGetOverlayColor(settings, out var overlayColor))
            {
                return;
            }

            canvas.SaveLayer();
            FillOverlay(canvas, settings, SKRect.Create(width, height), overlayColor);
            punch(canvas, overlayColor);
            canvas.Restore();

            afterRestore?.Invoke(canvas, overlayColor);
        }

        // CreatePunchPaint
        // Erases whatever is drawn with it out of the overlay layer. A blur radius feathers the
        // edge, which reads as paint rather than as a digital cut.
        protected static SKPaint CreatePunchPaint(float blurRadius = 0f)
        {
            var paint = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Fill,
                BlendMode = SKBlendMode.DstOut,
                IsAntialias = true
            };

            if (blurRadius > 0f)
            {
                // SKPaint does not own its mask filter, so it is disposed with the paint below.
                paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius);
            }

            return paint;
        }

        // CreateOutlinePaint
        // The contrasting line traced around a punched hole, shared by every style that offers the
        // cutout border toggle.
        protected static SKPaint CreateOutlinePaint(SKColor overlayColor, float strokeWidth)
        {
            return new SKPaint
            {
                Color = ColorUtils.GetContrastingOutline(overlayColor),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1f, strokeWidth),
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round
            };
        }

        // CreateOverlayGradient
        // Creates a shader for the specified gradient direction.
        protected virtual SKShader? CreateOverlayGradient(OverlayGradient gradientType, SKRect rect, SKColor primaryColor, SKColor secondaryColor)
        {
            var colors = new[] { primaryColor, secondaryColor };

            return gradientType switch
            {
                OverlayGradient.LeftToRight => SKShader.CreateLinearGradient(
                    new SKPoint(rect.Left, rect.MidY),
                    new SKPoint(rect.Right, rect.MidY),
                    colors, null, SKShaderTileMode.Clamp, SKMatrix.Identity),

                OverlayGradient.BottomToTop => SKShader.CreateLinearGradient(
                    new SKPoint(rect.MidX, rect.Bottom),
                    new SKPoint(rect.MidX, rect.Top),
                    colors, null, SKShaderTileMode.Clamp, SKMatrix.Identity),

                OverlayGradient.TopLeftCornerToBottomRightCorner => SKShader.CreateLinearGradient(
                    new SKPoint(rect.Left, rect.Top),
                    new SKPoint(rect.Right, rect.Bottom),
                    colors, null, SKShaderTileMode.Clamp, SKMatrix.Identity),

                OverlayGradient.TopRightCornerToBottomLeftCorner => SKShader.CreateLinearGradient(
                    new SKPoint(rect.Right, rect.Top),
                    new SKPoint(rect.Left, rect.Bottom),
                    colors, null, SKShaderTileMode.Clamp, SKMatrix.Identity),

                _ => null
            };
        }
    }
}
