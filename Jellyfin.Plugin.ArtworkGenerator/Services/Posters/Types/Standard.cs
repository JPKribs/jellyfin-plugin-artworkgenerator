using System;
using SkiaSharp;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Posters
{
    public class StandardPosterGenerator : BasePosterGenerator
    {
        // Style
        // The poster style this generator produces.
        public override PosterStyle Style => PosterStyle.Standard;

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        public override string Description => "Full frame image with text at the bottom. Clean and versatile.";

        // StandardPosterGenerator
        // Initializes a new instance of the standard poster generator with logging support.
        public StandardPosterGenerator(ILogger<StandardPosterGenerator> logger)
            : base(logger)
        {
        }

        // ApplyFrameExtraction
        // Exposes the shared stamping for the tests, so what they check is what the renderer calls.
        internal static PosterSettings ApplyFrameExtraction(PosterSettings settings, FrameExtractionSettings extraction)
            => WithFrameExtraction(settings, extraction);

        // DrawsSecondary
        // Exposes the shared rule for the tests, so what they check is what the designs call.
        internal static bool DrawsSecondary(PosterSettings settings, ArtworkSubject subject)
            => ShowsSecondary(settings, subject);

        // RenderTypography
        // Renders the identity line, a rule, and the title at the bottom of the poster.
        protected override void RenderTypography(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var safeArea = GetSafeAreaBounds(width, height, settings);
            DrawTextStack(skCanvas, safeArea, subject, settings, SizeUnit(width, height));
        }

    }
}
