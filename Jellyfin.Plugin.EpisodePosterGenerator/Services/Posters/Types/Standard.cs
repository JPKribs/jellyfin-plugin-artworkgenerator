using System;
using SkiaSharp;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Services.Posters
{
    public class StandardPosterGenerator : BasePosterGenerator
    {
        // Style
        // The poster style this generator produces.
        public override PosterStyle Style => PosterStyle.Standard;

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        public override string Description => "Full frame image with text at the bottom. Clean and versatile.";

        private readonly ILogger<StandardPosterGenerator> _logger;

        // StandardPosterGenerator
        // Initializes a new instance of the standard poster generator with logging support.
        public StandardPosterGenerator(ILogger<StandardPosterGenerator> logger)
        {
            _logger = logger;
        }

        // RenderTypography
        // Renders the identity line, a rule, and the title at the bottom of the poster.
        protected override void RenderTypography(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var safeArea = GetSafeAreaBounds(width, height, settings);
            DrawBottomTextStack(skCanvas, safeArea, subject, settings, SizeUnit(width, height));
        }

        // LogError
        // Logs an error that occurred during standard poster generation.
        protected override void LogError(Exception ex, string? episodeName)
        {
            _logger.LogError(ex, "Failed to generate standard poster for {EpisodeName}", episodeName);
        }
    }
}
