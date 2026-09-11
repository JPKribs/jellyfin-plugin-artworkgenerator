using System;
using System.Collections.Generic;
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

        // The numeral is the poster and is sized to fill it, so it is always drawn and its size
        // setting would do nothing.
        public override IReadOnlyDictionary<string, PosterSettingState> SettingRules => PosterSettingRules.Build(
            (PosterSettingRules.ShowEpisode, PosterSettingState.Required),
            (PosterSettingRules.EpisodeFontSize, PosterSettingState.Hidden));

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

            // A series has no number, so its name is the focal element and is not drawn twice.
            if (!subject.Number.HasValue)
            {
                if (!string.IsNullOrEmpty(subject.Title))
                {
                    DrawFocalText(skCanvas, subject.Title, settings, safeArea, unit);
                }

                return;
            }

            using var titleStyle = CreateTitleStyle(settings, unit);
            var showTitle = settings.ShowTitle && !string.IsNullOrEmpty(subject.Title);

            // The title's zone is reserved before the numeral is sized, so the numeral fills what is
            // left rather than being drawn across the title.
            var column = new LayoutColumn(safeArea, GetElementSpacing(settings, unit), LayoutAnchor.Bottom)
                .Add(TitleBlock, showTitle ? titleStyle.BlockHeight(2) : 0f);

            DrawFocalText(skCanvas, NumberUtils.NumberToRomanNumeral(subject.Number.Value), settings, column.Remaining, unit);

            if (column.TryGetSlot(TitleBlock, out var titleSlot))
            {
                DrawTitleInSlot(skCanvas, subject.Title!, titleStyle, titleSlot, titleSlot.MidX, titleSlot.Width * RenderConstants.TextWidthMultiplier, settings.LongTitleHandling);
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
