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
        // A series has no number, so its name becomes the focal element instead.
        protected override void RenderTypography(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var unit = SizeUnit(width, height);
            var safeArea = GetSafeAreaBounds(width, height, settings);

            if (subject.Number.HasValue)
            {
                DrawFocalText(skCanvas, NumberUtils.NumberToRomanNumeral(subject.Number.Value), settings, safeArea, unit);
            }
            else if (!string.IsNullOrEmpty(subject.Title))
            {
                // Drawn once, as the focal text, rather than again as the overlapping title.
                DrawFocalText(skCanvas, subject.Title, settings, safeArea, unit);
                return;
            }

            if (settings.ShowTitle && !string.IsNullOrEmpty(subject.Title))
            {
                using var titleStyle = CreateTitleStyle(settings, unit);
                DrawTitleInSlot(skCanvas, subject.Title, titleStyle, safeArea, safeArea.MidX, safeArea.Width * RenderConstants.TextWidthMultiplier, settings.LongTitleHandling);
            }
        }

        // LogError
        // Logs an error that occurred during numeral poster generation.
        protected override void LogError(Exception ex, string? episodeName)
        {
            _logger.LogError(ex, "Failed to generate numeral poster for {EpisodeName}", episodeName);
        }

        // DrawFocalText
        // Draws the focal text, a Roman numeral or a series name, sized to fill, and centred on its
        // ink in, the area.
        private static void DrawFocalText(SKCanvas canvas, string numeralText, PosterSettings config, SKRect area, int unit)
        {
            var typeface = ResolveEpisodeTypeface(config, FontUtils.GetFontStyle(config.EpisodeFontStyle));

            float fontSize = FontUtils.CalculateOptimalFontSize(numeralText, typeface, area.Width, area.Height);

            using var style = PaintFactory.CreateTextStyle(ColorUtils.ParseHexColor(config.EpisodeFontColor), fontSize, typeface, unit);

            var bounds = style.MeasureBounds(numeralText);
            style.Draw(canvas, numeralText, area.MidX, area.MidY - bounds.MidY);
        }
    }
}
