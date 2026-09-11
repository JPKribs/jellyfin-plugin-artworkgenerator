using System;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Utilities;
using SkiaSharp;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Services.Posters
{
    public class NumeralPosterGenerator : BasePosterGenerator
    {
        // Style
        // The poster style this generator produces.
        public override PosterStyle Style => PosterStyle.Numeral;

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        public override string Description => "Large Roman numeral as the focal element. Minimal and distinctive.";

        private readonly ILogger<NumeralPosterGenerator> _logger;

        // NumeralPosterGenerator
        // Initializes a new instance of the numeral poster generator with logging support.
        public NumeralPosterGenerator(ILogger<NumeralPosterGenerator> logger)
        {
            _logger = logger;
        }

        // RenderTypography
        // Renders the Roman numeral filling the safe area, with the optional title centred over it.
        protected override void RenderTypography(SKCanvas skCanvas, EpisodeMetadata episodeMetadata, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(episodeMetadata);
            ArgumentNullException.ThrowIfNull(settings);

            var safeArea = GetSafeAreaBounds(width, height, settings);

            DrawRomanNumeral(skCanvas, episodeMetadata, settings, safeArea, height);

            if (settings.ShowTitle && !string.IsNullOrEmpty(episodeMetadata.EpisodeName))
            {
                using var titleStyle = CreateTitleStyle(settings, height);
                DrawTitleInSlot(skCanvas, episodeMetadata.EpisodeName, titleStyle, safeArea, safeArea.MidX, safeArea.Width * RenderConstants.TextWidthMultiplier, settings.LongTitleHandling);
            }
        }

        // LogError
        // Logs an error that occurred during numeral poster generation.
        protected override void LogError(Exception ex, string? episodeName)
        {
            _logger.LogError(ex, "Failed to generate numeral poster for {EpisodeName}", episodeName);
        }

        // DrawRomanNumeral
        // Draws the episode number as a Roman numeral sized to fill, and centred on its ink in, the area.
        private static void DrawRomanNumeral(SKCanvas canvas, EpisodeMetadata episodeMetadata, PosterSettings config, SKRect area, int height)
        {
            var numeralText = NumberUtils.NumberToRomanNumeral(episodeMetadata.EpisodeNumberStart ?? 0);
            var typeface = ResolveEpisodeTypeface(config, FontUtils.GetFontStyle(config.EpisodeFontStyle));

            float fontSize = FontUtils.CalculateOptimalFontSize(numeralText, typeface, area.Width, area.Height);

            using var style = PaintFactory.CreateTextStyle(ColorUtils.ParseHexColor(config.EpisodeFontColor), fontSize, typeface, height);

            var bounds = style.MeasureBounds(numeralText);
            style.Draw(canvas, numeralText, area.MidX, area.MidY - bounds.MidY);
        }
    }
}
