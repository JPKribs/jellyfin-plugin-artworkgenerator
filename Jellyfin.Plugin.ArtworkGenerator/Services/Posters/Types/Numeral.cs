using System;
using System.Collections.Generic;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using SkiaSharp;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Posters
{
    public class NumeralPosterGenerator : BasePosterGenerator
    {
        // Style
        // The poster style this generator produces.
        public override PosterStyle Style => PosterStyle.Numeral;

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        public override string Description => "Large Roman numeral as the focal element. Minimal and distinctive.";

        // PrimaryDescription
        // One sentence on what the title is and where this style puts it.
        public override string PrimaryDescription
            => "The title is the item's own name, set under the numeral, and it takes the numeral's place when the item has no number.";

        // SecondaryDescription
        // One sentence on what the subtitle is and where this style puts it.
        public override string SecondaryDescription
            => "The subtitle is the item's number, drawn as the large Roman numeral rather than as a line of text.";

        // The numeral is the poster and is sized to fill it, so it is always drawn and its size
        // setting would do nothing.
        public override IReadOnlyDictionary<string, PosterSettingState> SettingRules => PosterSettingRules.Build(
            (PosterSettingRules.ShowSecondary, PosterSettingState.Required),
            (PosterSettingRules.SecondaryFontSize, PosterSettingState.Hidden));

        // NumeralPosterGenerator
        // Initializes a new instance of the numeral poster generator with logging support.
        public NumeralPosterGenerator(ILogger<NumeralPosterGenerator> logger)
            : base(logger)
        {
        }

        // RenderTypography
        // Renders the Roman numeral filling the safe area, with the optional title centered over it.
        // A series has no number, so its name becomes the focal element instead.
        protected override void RenderTypography(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var unit = SizeUnit(width, height);
            var safeArea = GetSafeAreaBounds(width, height, settings);

            // A series has no number, so its name is the focal element and is not drawn twice.
            if (!subject.FeaturedNumber.HasValue)
            {
                if (!string.IsNullOrEmpty(subject.Primary))
                {
                    DrawFocalText(skCanvas, subject.Primary, settings, safeArea, unit);
                }

                return;
            }

            using var primaryStyle = CreatePrimaryStyle(settings, unit);
            var showPrimary = ShowsPrimary(settings, subject);

            // The title's zone is reserved before the numeral is sized, so the numeral fills what is
            // left rather than being drawn across the title.
            var column = new LayoutColumn(safeArea, GetElementSpacing(settings, unit), ResolveTextAnchor(settings))
                .Add(PrimaryBlock, showPrimary ? primaryStyle.BlockHeight(2) : 0f);

            DrawFocalText(skCanvas, NumberUtils.NumberToRomanNumeral(subject.FeaturedNumber.Value), settings, column.Remaining, unit);

            if (column.TryGetSlot(PrimaryBlock, out var primarySlot))
            {
                DrawPrimaryInSlot(skCanvas, subject.Primary!, primaryStyle, primarySlot, primarySlot.MidX, primarySlot.Width * RenderConstants.TextWidthMultiplier, settings.LongTextHandling);
            }
        }


        // DrawFocalText
        // Draws the focal text, a Roman numeral or a series name, sized to fill, and centered on its
        // ink in, the area.
        private static void DrawFocalText(SKCanvas canvas, string numeralText, PosterSettings config, SKRect area, int unit)
        {
            var typeface = ResolveSecondaryTypeface(config);

            float fontSize = FontUtils.CalculateOptimalFontSize(numeralText, typeface, area.Width, area.Height);

            using var style = PaintFactory.CreateTextStyle(ColorUtils.ParseHexColor(config.SecondaryFontColor), fontSize, typeface, unit);

            var bounds = style.MeasureBounds(numeralText);
            style.Draw(canvas, numeralText, area.MidX, area.MidY - bounds.MidY);
        }
    }
}
