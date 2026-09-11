using System;
using SkiaSharp;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Utilities
{
    // RenderConstants
    // Centralized constants for consistent rendering across all poster types.
    //
    // Pixel sizes are expressed at a 1080 pixel tall reference poster and scaled to the poster
    // actually being drawn. A shadow offset or rule width that is fixed in pixels reads as a
    // hairline on a 4K frame and as a slab on a DVD rip; scaling keeps the same visual weight
    // at every resolution the extractor can hand us.
    public static class RenderConstants
    {
        // Poster height every reference size below is expressed against.
        public const float ReferenceHeight = 1080f;

        // Text rendering constants
        public const float LineHeightMultiplier = 1.2f;
        public const float TextWidthMultiplier = 0.9f;
        public const byte ShadowAlpha = 180;

        // Reference sizes, in pixels at ReferenceHeight.
        private const float ShadowOffsetReference = 2f;
        private const float ShadowBlurSigmaReference = 1.5f;
        private const float SeparatorStrokeReference = 2f;
        private const float SeparatorSlotReference = 4f;

        // JPEG encode quality for posters and backdrops. 92 is visually indistinguishable
        // from 100 at poster resolution but produces files a fraction of the size.
        public const int JpegQuality = 92;

        // Shadow color (black with standard alpha)
        public static SKColor ShadowColor => SKColors.Black.WithAlpha(ShadowAlpha);

        // Mitchell-Netravali cubic resampling: sharp enough for logos and artwork being
        // scaled, soft enough not to ring on hard edges. Replaces the removed FilterQuality.High.
        public static SKSamplingOptions HighQualitySampling { get; } = new SKSamplingOptions(new SKCubicResampler(1f / 3f, 1f / 3f));

        // Bilinear with mipmaps, for the small analysis thumbnails where speed matters more.
        public static SKSamplingOptions FastSampling { get; } = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);

        // Scaled
        // Converts a size given at the reference height to this poster's pixels, never
        // dropping below a single pixel so small posters keep a visible stroke.
        public static float Scaled(float referencePixels, float posterHeight)
        {
            return Math.Max(1f, referencePixels * Math.Max(1f, posterHeight) / ReferenceHeight);
        }

        public static float ShadowOffset(float posterHeight) => Scaled(ShadowOffsetReference, posterHeight);

        public static float ShadowBlurSigma(float posterHeight) => Scaled(ShadowBlurSigmaReference, posterHeight);

        public static float SeparatorStrokeWidth(float posterHeight) => Scaled(SeparatorStrokeReference, posterHeight);

        public static float SeparatorSlotHeight(float posterHeight) => Scaled(SeparatorSlotReference, posterHeight);
    }
}
