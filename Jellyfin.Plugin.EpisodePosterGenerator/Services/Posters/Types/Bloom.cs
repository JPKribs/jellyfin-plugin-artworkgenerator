using System;
using System.Collections.Generic;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Utilities;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Services.Posters
{
    public class BloomPosterGenerator : BasePosterGenerator
    {
        // How far the bloom reaches, as a share of the size unit, so the glow keeps the same
        // weight on a portrait poster as on a landscape one rather than stretching with the frame.
        private const float RadiusRatio = 0.62f;

        // The bloom holds its full colour this far out before it begins falling away, which keeps
        // a readable pool of colour behind the text instead of a single bright point.
        private const float CoreStop = 0.18f;

        // Share of the safe width the text may use, so lines wrap inside the bloom rather than
        // running out into the uncoloured corners.
        private const float TextWidthRatio = 0.8f;

        // Style
        // The poster style this generator produces.
        public override PosterStyle Style => PosterStyle.Bloom;

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        public override string Description => "A soft bloom of colour in the centre with the text set over it.";

        private readonly ILogger<BloomPosterGenerator> _logger;

        // BloomPosterGenerator
        // Initializes a new instance of the bloom poster generator with logging support.
        public BloomPosterGenerator(ILogger<BloomPosterGenerator> logger)
        {
            _logger = logger;
        }

        // RenderOverlay
        // Draws a radial bloom centred on the poster: the overlay colour at full strength in the
        // middle, falling away to the secondary colour at the rim, or to nothing when no secondary
        // colour is set so the frame shows through at the corners.
        protected override void RenderOverlay(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(skCanvas);
            ArgumentNullException.ThrowIfNull(settings);

            if (string.IsNullOrEmpty(settings.OverlayColor))
            {
                return;
            }

            var centreColor = ColorUtils.ParseHexColor(settings.OverlayColor);
            if (centreColor.Alpha == 0)
            {
                return;
            }

            // A zero-alpha secondary keeps the meaning it has everywhere else in the plugin: there
            // is no second colour, so the bloom simply fades out.
            var edgeColor = string.IsNullOrEmpty(settings.OverlaySecondaryColor)
                ? centreColor.WithAlpha(0)
                : ColorUtils.ParseHexColor(settings.OverlaySecondaryColor);
            if (edgeColor.Alpha == 0)
            {
                edgeColor = centreColor.WithAlpha(0);
            }

            var rect = SKRect.Create(width, height);
            var radius = SizeUnit(width, height) * RadiusRatio;

            // Clamp means everything past the radius keeps the rim colour, so the corners of a wide
            // poster are covered by the same colour the bloom ends on.
            using var shader = SKShader.CreateRadialGradient(
                new SKPoint(rect.MidX, rect.MidY),
                radius,
                new[] { centreColor, centreColor, edgeColor },
                new[] { 0f, CoreStop, 1f },
                SKShaderTileMode.Clamp);

            using var overlayPaint = new SKPaint
            {
                Shader = shader,
                Style = SKPaintStyle.Fill,
                IsDither = true
            };
            skCanvas.DrawRect(rect, overlayPaint);
        }

        // RenderTypography
        // Draws the secondary line above the primary one, the pair centred on the middle of the
        // poster so they sit in the brightest part of the bloom.
        protected override void RenderTypography(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(skCanvas);
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var unit = SizeUnit(width, height);
            var safeArea = GetSafeAreaBounds(width, height, settings);

            using var primaryStyle = CreatePrimaryStyle(settings, unit, SKTextAlign.Center);
            using var secondaryStyle = CreateSecondaryStyle(settings, unit, SKTextAlign.Center);

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
                SKRect.Create(safeArea.Left, safeArea.MidY - (consumed / 2f), safeArea.Width, consumed),
                spacing,
                LayoutAnchor.Top)
                .Add(SecondaryBlock, secondaryHeight)
                .Add(PrimaryBlock, primaryHeight);

            if (secondaryText != null && placed.TryGetSlot(SecondaryBlock, out var secondarySlot))
            {
                secondaryStyle.Draw(skCanvas, secondaryText, safeArea.MidX, secondaryStyle.BaselineAtTop(secondarySlot));
            }

            if (primaryLines.Count > 0 && placed.TryGetSlot(PrimaryBlock, out var primarySlot))
            {
                primaryStyle.DrawLines(skCanvas, primaryLines, safeArea.MidX, primaryStyle.BaselineAtTop(primarySlot));
            }
        }

        // LogError
        // Logs an error that occurred during bloom poster generation.
        protected override void LogError(Exception ex, string? episodeName)
        {
            _logger.LogError(ex, "Failed to generate bloom poster for {EpisodeName}", episodeName);
        }
    }
}
