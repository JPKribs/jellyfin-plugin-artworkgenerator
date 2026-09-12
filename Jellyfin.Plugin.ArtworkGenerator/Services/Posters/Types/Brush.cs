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

        // NaturalTextAlignment
        // Left rather than the center most designs use: its text sits in a left hand column over the strokes.
        protected override TextAlignment NaturalTextAlignment => TextAlignment.Left;

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

        // BrushPosterGenerator
        // Initializes a new instance of the brush poster generator with logging support.
        public BrushPosterGenerator(ILogger<BrushPosterGenerator> logger)
            : base(logger)
        {
        }

        // RenderOverlay
        // Creates an overlay with brush stroke cutouts revealing the canvas beneath.
        protected override void RenderOverlay(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var unit = SizeUnit(width, height);
            var safeArea = GetSafeAreaBounds(width, height, settings);

            // Seed from the subject's source path so the same item always produces the same stroke
            // layout, but different items vary. Falls back to series id + season + episode when
            // there is no path (the preview and demo generator).
            var strokeBuilder = new BrushStrokeBuilder(GenerateBrushSeed(subject));

            // The strokes are the design, so they are laid down whole. Text is drawn over them with
            // its own shadow and reads fine; cutting a hole for it left a rectangle in the paint
            // that looked far worse than the overlap it avoided.
            using var brushMask = strokeBuilder.BuildStrokePath(safeArea, unit);

            DrawPunchedOverlay(
                skCanvas,
                settings,
                width,
                height,
                // A slightly blurred punch: the feathered edge reads as paint on canvas, where a
                // hard clip edge reads as a digital cut.
                (canvas, _) =>
                {
                    using var punchPaint = CreatePunchPaint(Math.Max(2f, unit * 0.002f));
                    canvas.DrawPath(brushMask, punchPaint);
                },
                // Traced after the layer is restored: inside it, the punch above would erase the
                // outline along with the overlay.
                (canvas, overlayColor) =>
                {
                    if (!settings.CutoutBorder)
                    {
                        return;
                    }

                    using var outlinePaint = CreateOutlinePaint(overlayColor, unit * 0.003f);
                    canvas.DrawPath(brushMask, outlinePaint);
                });
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

        // RenderTypography
        // Renders the code and title in the bottom left corner of the poster.
        protected override void RenderTypography(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var unit = SizeUnit(width, height);
            var safeArea = GetSafeAreaBounds(width, height, settings);

            var align = ResolveTextAlign(settings);
            using var secondaryStyle = CreateSecondaryStyle(settings, unit, align);
            using var primaryStyle = CreatePrimaryStyle(settings, unit, align);

            var column = BuildTextColumn(safeArea, settings, unit, subject, secondaryStyle, primaryStyle);

            if (column.TryGetSlot(SecondaryBlock, out var codeSlot))
            {
                DrawFittedLine(skCanvas, secondaryStyle, subject.SecondaryShort, AlignedX(safeArea, align), secondaryStyle.BaselineAtBottom(codeSlot), safeArea.Width * TextWidthRatio, settings.LongSubtitleHandling);
            }

            if (column.TryGetSlot(PrimaryBlock, out var primarySlot))
            {
                DrawPrimaryInSlot(skCanvas, subject.Primary!, primaryStyle, primarySlot, AlignedX(safeArea, align), safeArea.Width * TextWidthRatio, settings.LongTextHandling);
            }
        }

    }
}
