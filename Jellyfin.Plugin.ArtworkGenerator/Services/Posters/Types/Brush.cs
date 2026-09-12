using System;
using System.Collections.Generic;
using SkiaSharp;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Posters
{
    public class BrushPosterGenerator : BasePosterGenerator
    {
        // Style
        // The poster style this generator produces.
        public override PosterStyle Style => PosterStyle.Brush;

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        public override string Description => "Brush strokes reveal the image through a flat overlay. Painted, editorial look.";

        // PrimaryDescription
        // One sentence on what the title is and where this style puts it.
        public override string PrimaryDescription
            => "The title is the item's own name, set left aligned at the foot of the image over the brush strokes.";

        // SecondaryDescription
        // One sentence on what the subtitle is and where this style puts it.
        public override string SecondaryDescription
            => "The subtitle is the episode code, set on the left just above the title.";

        // The stroke carries the title and the code, so both are always drawn, and the stroke can
        // take the same outline the cutout uses.
        public override IReadOnlyDictionary<string, PosterSettingState> SettingRules => PosterSettingRules.Build(
            (PosterSettingRules.CutoutBorder, PosterSettingState.Optional),
            (PosterSettingRules.ShowPrimary, PosterSettingState.Required),
            (PosterSettingRules.ShowSecondary, PosterSettingState.Required));

        // Share of the safe width the text may use. The stroke keep-clear zone is measured from
        // the same figure, so a wrapped title can never run under a stroke edge.
        private const float TextWidthRatio = 0.6f;

        private readonly ILogger<BrushPosterGenerator> _logger;

        // BrushPosterGenerator
        // Initializes a new instance of the brush poster generator with logging support.
        public BrushPosterGenerator(ILogger<BrushPosterGenerator> logger)
        {
            _logger = logger;
        }

        // RenderOverlay
        // Creates an overlay with brush stroke cutouts revealing the canvas beneath.
        protected override void RenderOverlay(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(skCanvas);
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            if (string.IsNullOrEmpty(settings.OverlayColor))
            {
                return;
            }

            var primaryColor = ColorUtils.ParseHexColor(settings.OverlayColor);
            if (primaryColor.Alpha == 0)
            {
                return;
            }

            var unit = SizeUnit(width, height);
            var rect = SKRect.Create(width, height);
            var safeArea = GetSafeAreaBounds(width, height, settings);
            var textArea = CalculateTextKeepClearArea(safeArea, settings, unit, subject);

            // Seed from the subject's source path so the same item always produces the same stroke
            // layout, but different items vary. Falls back to series id + season + episode when
            // there is no path (the preview and demo generator).
            var seed = GenerateBrushSeed(subject);
            var strokeBuilder = new BrushStrokeBuilder(seed);
            using var brushMask = strokeBuilder.BuildStrokePath(safeArea, textArea, unit);

            // Draw the overlay into its own layer, then erase the stroke mask out of it with a
            // slightly blurred punch. The feathered edge reads as paint on canvas; a hard ClipPath
            // edge reads as a digital cut.
            skCanvas.SaveLayer();

            if (settings.OverlayGradient == OverlayGradient.None)
            {
                using var overlayPaint = PaintFactory.CreateFillPaint(primaryColor);
                skCanvas.DrawRect(rect, overlayPaint);
            }
            else
            {
                var secondaryColor = ColorUtils.ParseHexColor(settings.OverlaySecondaryColor);
                if (secondaryColor.Alpha == 0)
                {
                    secondaryColor = primaryColor;
                }

                using var gradient = CreateOverlayGradient(settings.OverlayGradient, rect, primaryColor, secondaryColor);
                if (gradient != null)
                {
                    using var overlayPaint = new SKPaint
                    {
                        Shader = gradient,
                        Style = SKPaintStyle.Fill,
                        IsDither = true
                    };
                    skCanvas.DrawRect(rect, overlayPaint);
                }
            }

            // SKPaint does not own its mask filter, hence the explicit using.
            using var punchBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(2f, unit * 0.002f));
            using var punchPaint = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Fill,
                BlendMode = SKBlendMode.DstOut,
                IsAntialias = true,
                MaskFilter = punchBlur
            };
            skCanvas.DrawPath(brushMask, punchPaint);

            skCanvas.Restore();

            // Optional outline tracing the stroke edge, sharing the Cutout style's border toggle
            // and its contrast rule. Drawn after the layer is restored: inside it, the DstOut
            // punch above would erase the outline along with the overlay.
            if (settings.CutoutBorder)
            {
                using var outlinePaint = new SKPaint
                {
                    Color = ColorUtils.GetContrastingOutline(primaryColor),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = Math.Max(1f, unit * 0.003f),
                    IsAntialias = true,
                    StrokeCap = SKStrokeCap.Round,
                    StrokeJoin = SKStrokeJoin.Round
                };
                skCanvas.DrawPath(brushMask, outlinePaint);
            }
        }

        // GenerateBrushSeed
        // Produces a deterministic int seed for a subject using a stable FNV-1a hash of its source
        // path. The string overload of GetHashCode() is randomized per process on modern .NET, so
        // the bytes are hashed here to keep posters stable across server restarts.
        private static int GenerateBrushSeed(ArtworkSubject metadata)
        {
            var path = metadata.VideoMetadata?.SourcePath;
            if (!string.IsNullOrEmpty(path))
            {
                unchecked
                {
                    int hash = (int)2166136261;
                    foreach (char c in path)
                    {
                        hash ^= c;
                        hash *= 16777619;
                    }

                    return hash;
                }
            }

            int fallback = 0;
            if (metadata.SeriesId != Guid.Empty)
            {
                var bytes = metadata.SeriesId.ToByteArray();
                fallback = BitConverter.ToInt32(bytes, 0)
                    ^ BitConverter.ToInt32(bytes, 4)
                    ^ BitConverter.ToInt32(bytes, 8)
                    ^ BitConverter.ToInt32(bytes, 12);
            }

            fallback = (fallback * 397) ^ (metadata.SeasonNumber ?? 0);
            fallback = (fallback * 397) ^ (metadata.EpisodeNumberStart ?? 1);
            return fallback;
        }

        // BuildTextColumn
        // The one description of the text layout: the code above a fixed two line title zone,
        // packed against the bottom left of the safe area.
        private LayoutColumn BuildTextColumn(SKRect safeArea, PosterSettings settings, int unit, ArtworkSubject subject, TextStyle secondaryStyle, TextStyle primaryStyle)
        {
            var primaryHeight = ShowsPrimary(settings, subject)
                ? primaryStyle.BlockHeight(2)
                : 0f;

            return new LayoutColumn(safeArea, GetElementSpacing(settings, unit), ResolveTextAnchor(settings))
                .Add(SecondaryBlock, ShowsSecondary(settings, subject) ? secondaryStyle.LineBox : 0f)
                .Add(PrimaryBlock, primaryHeight);
        }

        // CalculateTextKeepClearArea
        // The area the strokes must leave alone: exactly the block the text column occupies,
        // measured from the same styles that draw it and placed by the same column.
        private SKRect CalculateTextKeepClearArea(SKRect safeArea, PosterSettings settings, int unit, ArtworkSubject subject)
        {
            using var secondaryStyle = CreateSecondaryStyle(settings, unit, SKTextAlign.Left);
            using var primaryStyle = CreatePrimaryStyle(settings, unit, SKTextAlign.Left);

            // The column reports where it put itself, so the strokes follow the text wherever the
            // position setting sends it rather than assuming it is still at the bottom.
            var span = BuildTextColumn(safeArea, settings, unit, subject, secondaryStyle, primaryStyle).Span;

            return new SKRect(
                safeArea.Left,
                span.Top,
                safeArea.Left + (safeArea.Width * TextWidthRatio),
                span.Bottom);
        }

        // RenderTypography
        // Renders the code and title in the bottom left corner of the poster.
        protected override void RenderTypography(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var unit = SizeUnit(width, height);
            var safeArea = GetSafeAreaBounds(width, height, settings);

            using var secondaryStyle = CreateSecondaryStyle(settings, unit, SKTextAlign.Left);
            using var primaryStyle = CreatePrimaryStyle(settings, unit, SKTextAlign.Left);

            var column = BuildTextColumn(safeArea, settings, unit, subject, secondaryStyle, primaryStyle);

            if (column.TryGetSlot(SecondaryBlock, out var codeSlot))
            {
                secondaryStyle.Draw(skCanvas, subject.SecondaryShort, safeArea.Left, secondaryStyle.BaselineAtBottom(codeSlot));
            }

            if (column.TryGetSlot(PrimaryBlock, out var primarySlot))
            {
                DrawPrimaryInSlot(skCanvas, subject.Primary!, primaryStyle, primarySlot, safeArea.Left, safeArea.Width * TextWidthRatio, settings.LongTextHandling);
            }
        }

        // LogError
        // Logs an error that occurred during brush poster generation.
        protected override void LogError(Exception ex, string? episodeName)
        {
            _logger.LogError(ex, "Failed to generate Brush poster for episode {EpisodeName}", episodeName);
        }
    }
}
