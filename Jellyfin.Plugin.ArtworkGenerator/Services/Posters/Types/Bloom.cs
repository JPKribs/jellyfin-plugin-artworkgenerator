using System;
using System.Collections.Generic;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Posters
{
    public class BloomPosterGenerator : BasePosterGenerator
    {
        // How far the bloom reaches, as a share of the size unit, so the glow keeps the same weight
        // on a portrait poster as on a landscape one rather than stretching with the frame. Kept
        // well under half the short side on purpose: a radius that runs past the frame leaves tint
        // on every edge, which reads as an overall haze instead of a pool of color.
        private const float RadiusRatio = 0.85f;

        // The bloom holds its full color only this far out, leaving nearly all of the radius to the
        // eased falloff. That is what spreads the color widely and gently; a long core concentrates
        // it into a dark disc with a visible edge instead.
        // The falloff is sampled at this many stops and eased rather than ramped straight down.
        // A straight ramp changes slope abruptly where the core ends, and the eye reads that as the
        // edge of a drawn disc; easing spreads it out so the pool reads as light.
        private const int FalloffSteps = 6;
        private const float CoreStop = 0.22f;

        // Share of the safe width the text may use, so lines wrap inside the bloom rather than
        // running out into the uncolored corners.
        private const float TextWidthRatio = 0.8f;

        // Style
        // The poster style this generator produces.
        public override PosterStyle Style => PosterStyle.Bloom;

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        public override string Description => "A soft bloom of color in the center with the text set over it.";

        // SettingText
        // The bloom is a pool of color with its own falloff, so a wash direction means nothing here and
        // the second color is the rim rather than a gradient end.
        public override IReadOnlyDictionary<string, SettingText> SettingText { get; }
            = new Dictionary<string, SettingText>(StringComparer.Ordinal)
            {
                [PosterSettingRules.OverlayColor] = new(
                    "Bloom Color",
                    "The color pooled in the middle of the frame."),
                [PosterSettingRules.OverlaySecondaryColor] = new(
                    "Rim Color",
                    "The color the bloom fades out to at the edges.")
            };

        // SettingRules
        // The bloom is a pool of color with its own falloff, so a wash direction means nothing here and
        // the second color is the rim rather than a gradient end.
        public override IReadOnlyDictionary<string, PosterSettingState> SettingRules => PosterSettingRules.Build(
            (PosterSettingRules.OverlayGradient, PosterSettingState.Hidden),
            (PosterSettingRules.OverlaySecondaryColor, PosterSettingState.Optional));

        // NaturalTextPosition
        // The text belongs over the bloom, so centered is this style's own placement rather than
        // the usual foot of the image.
        protected override TextPosition NaturalTextPosition => TextPosition.Center;

        // PrimaryDescription
        // One sentence on what the title is and where this style puts it.
        public override string PrimaryDescription
            => "The title is the item's own name, centered over the bloom.";

        // SecondaryDescription
        // One sentence on what the subtitle is and where this style puts it.
        public override string SecondaryDescription
            => "The subtitle is the episode code, centered over the bloom just above the title.";

        // BloomPosterGenerator
        // Initializes a new instance of the bloom poster generator with logging support.
        public BloomPosterGenerator(ILogger<BloomPosterGenerator> logger)
            : base(logger)
        {
        }

        // RenderOverlay
        // Draws a radial bloom centered on the poster: the overlay color at full strength in the
        // middle, falling away to the secondary color at the rim, or to nothing when no secondary
        // color is set so the frame shows through at the corners.
        protected override void RenderOverlay(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(skCanvas);
            ArgumentNullException.ThrowIfNull(settings);

            if (!TryGetOverlayColor(settings, out var centerColor))
            {
                return;
            }

            // A zero-alpha secondary keeps the meaning it has everywhere else in the plugin: there
            // is no second color, so the bloom simply fades out.
            var edgeColor = string.IsNullOrEmpty(settings.OverlaySecondaryColor)
                ? centerColor.WithAlpha(0)
                : ColorUtils.ParseHexColor(settings.OverlaySecondaryColor);
            if (edgeColor.Alpha == 0)
            {
                edgeColor = centerColor.WithAlpha(0);
            }

            var rect = SKRect.Create(width, height);
            var radius = SizeUnit(width, height) * RadiusRatio;

            // Clamp means everything past the radius keeps the rim color, so the corners of a wide
            // poster are covered by the same color the bloom ends on.
            var colors = new SKColor[FalloffSteps + 2];
            var positions = new float[FalloffSteps + 2];

            colors[0] = centerColor;
            positions[0] = 0f;
            colors[1] = centerColor;
            positions[1] = CoreStop;

            for (int step = 1; step <= FalloffSteps; step++)
            {
                var travelled = step / (float)FalloffSteps;

                // Smoothstep: leaves the core gently instead of turning a corner there, and arrives
                // at the rim gently instead of stopping dead.
                var eased = travelled * travelled * (3f - (2f * travelled));

                colors[step + 1] = Blend(centerColor, edgeColor, eased);
                positions[step + 1] = CoreStop + (travelled * (1f - CoreStop));
            }

            using var shader = SKShader.CreateRadialGradient(
                new SKPoint(rect.MidX, rect.MidY),
                radius,
                colors,
                positions,
                SKShaderTileMode.Clamp);

            using var overlayPaint = new SKPaint
            {
                Shader = shader,
                Style = SKPaintStyle.Fill,
                IsDither = true
            };
            skCanvas.DrawRect(rect, overlayPaint);
        }

        // Blend
        // Mixes two colors, alpha included. A bloom that fades out has an edge color holding the
        // center's own channels at zero alpha, so mixing towards it fades without drifting in hue.
        private static SKColor Blend(SKColor from, SKColor to, float amount)
        {
            static byte Mix(byte a, byte b, float t) => (byte)Math.Clamp(a + ((b - a) * t), 0f, 255f);

            return new SKColor(
                Mix(from.Red, to.Red, amount),
                Mix(from.Green, to.Green, amount),
                Mix(from.Blue, to.Blue, amount),
                Mix(from.Alpha, to.Alpha, amount));
        }

        // RenderTypography
        // Draws the secondary line above the primary one, the pair centered on the middle of the
        // poster so they sit in the brightest part of the bloom.
        protected override void RenderTypography(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(skCanvas);
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var unit = SizeUnit(width, height);
            var safeArea = GetSafeAreaBounds(width, height, settings);

            var align = ResolveTextAlign(settings);
            using var primaryStyle = CreatePrimaryStyle(settings, unit, align);
            using var secondaryStyle = CreateSecondaryStyle(settings, unit, align);

            float maxTextWidth = safeArea.Width * TextWidthRatio;

            var primaryLines = new List<string>();
            if (ShowsPrimary(settings, subject))
            {
                primaryLines.AddRange(TextUtils.FitTextLines(subject.Primary!, primaryStyle.Font, maxTextWidth, settings.LongTextHandling));
            }

            var shortText = subject.SecondaryShort;
            string? secondaryText = ShowsSecondary(settings, subject) && shortText.Length > 0 ? shortText : null;

            if (primaryLines.Count == 0 && secondaryText == null)
            {
                return;
            }

            float spacing = GetElementSpacing(settings, unit);

            // Measured and placed by the same column, so the block cannot be sized from one set of
            // numbers and filled from another: the first pass only reports how tall the pair is.
            float secondaryHeight = secondaryText != null ? secondaryStyle.LineBox : 0f;
            float primaryHeight = primaryLines.Count > 0 ? primaryStyle.BlockHeight(primaryLines.Count) : 0f;

            var measured = new LayoutColumn(SKRect.Create(safeArea.Left, 0, safeArea.Width, 0), spacing, LayoutAnchor.Top)
                .Add(SecondaryBlock, secondaryHeight)
                .Add(PrimaryBlock, primaryHeight);

            float consumed = measured.Consumed;

            var placed = new LayoutColumn(
                SKRect.Create(safeArea.Left, PlaceBlockTop(safeArea, consumed, settings), safeArea.Width, consumed),
                spacing,
                LayoutAnchor.Top)
                .Add(SecondaryBlock, secondaryHeight)
                .Add(PrimaryBlock, primaryHeight);

            if (secondaryText != null && placed.TryGetSlot(SecondaryBlock, out var secondarySlot))
            {
                DrawFittedLine(skCanvas, secondaryStyle, secondaryText, AlignedX(safeArea, align), secondaryStyle.BaselineAtTop(secondarySlot), maxTextWidth, settings.LongSubtitleHandling);
            }

            if (primaryLines.Count > 0 && placed.TryGetSlot(PrimaryBlock, out var primarySlot))
            {
                primaryStyle.DrawLines(skCanvas, primaryLines, AlignedX(safeArea, align), primaryStyle.BaselineAtTop(primarySlot));
            }
        }

    }
}
