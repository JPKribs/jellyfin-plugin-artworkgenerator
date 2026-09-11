using System;
using SkiaSharp;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Utilities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Services.Posters
{
    public class BrushPosterGenerator : BasePosterGenerator
    {
        // Style
        // The poster style this generator produces.
        public override PosterStyle Style => PosterStyle.Brush;

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        public override string Description => "Brush strokes reveal the image through a flat overlay. Painted, editorial look.";

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
        protected override void RenderOverlay(SKCanvas skCanvas, EpisodeMetadata episodeMetadata, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(skCanvas);
            ArgumentNullException.ThrowIfNull(episodeMetadata);
            ArgumentNullException.ThrowIfNull(settings);

            if (string.IsNullOrEmpty(settings.OverlayColor))
                return;

            var primaryColor = ColorUtils.ParseHexColor(settings.OverlayColor);
            if (primaryColor.Alpha == 0)
                return;

            var rect = SKRect.Create(width, height);
            var safeArea = GetSafeAreaBounds(width, height, settings);
            var textArea = CalculateTextKeepClearArea(safeArea, settings, height, episodeMetadata);

            // Seed from the episode's file path so the same episode always produces the
            // same stroke layout, but different episodes vary. Falls back to series id +
            // season + episode if the file path isn't populated (e.g. demo generator).
            var seed = GenerateBrushSeed(episodeMetadata);
            var strokeBuilder = new BrushStrokeBuilder(seed);
            using var brushMask = strokeBuilder.BuildStrokePath(safeArea, textArea, height);

            // Draw the overlay into its own layer, then erase the stroke mask out of it with
            // a slightly blurred punch. The feathered edge reads as paint on canvas; a hard
            // ClipPath edge reads as a digital cut.
            skCanvas.SaveLayer();

            if (settings.OverlayGradient == OverlayGradient.None)
            {
                using var overlayPaint = PaintFactory.CreateFillPaint(primaryColor);
                skCanvas.DrawRect(rect, overlayPaint);
            }
            else
            {
                var secondaryColor = ColorUtils.ParseHexColor(settings.OverlaySecondaryColor);
                if (secondaryColor.Alpha == 0) secondaryColor = primaryColor;

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
            using var punchBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(2f, height * 0.002f));
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
                    StrokeWidth = Math.Max(1f, height * 0.003f),
                    IsAntialias = true,
                    StrokeCap = SKStrokeCap.Round,
                    StrokeJoin = SKStrokeJoin.Round
                };
                skCanvas.DrawPath(brushMask, outlinePaint);
            }
        }

        // GenerateBrushSeed
        // Produces a deterministic int seed for an episode using a stable FNV-1a hash of
        // the episode's file path. The string overload of GetHashCode() is randomized per
        // process on modern .NET, so we hash the bytes ourselves to keep posters stable
        // across server restarts.
        private static int GenerateBrushSeed(EpisodeMetadata metadata)
        {
            var path = metadata.VideoMetadata?.EpisodeFilePath;
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
        // The one description of the text layout: episode code above a fixed two line title
        // zone, packed against the bottom left of the safe area.
        private static LayoutColumn BuildTextColumn(SKRect safeArea, PosterSettings settings, int height, EpisodeMetadata episodeMetadata, TextStyle episodeStyle, TextStyle titleStyle)
        {
            var titleHeight = settings.ShowTitle && !string.IsNullOrWhiteSpace(episodeMetadata.EpisodeName)
                ? titleStyle.BlockHeight(2)
                : 0f;

            return new LayoutColumn(safeArea, GetElementSpacing(settings, height), LayoutAnchor.Bottom)
                .Add(EpisodeBlock, settings.ShowEpisode ? episodeStyle.LineBox : 0f)
                .Add(TitleBlock, titleHeight);
        }

        // CalculateTextKeepClearArea
        // The area the strokes must leave alone: exactly the block the text column occupies,
        // measured from the same styles that draw it.
        private static SKRect CalculateTextKeepClearArea(SKRect safeArea, PosterSettings settings, int height, EpisodeMetadata episodeMetadata)
        {
            using var episodeStyle = CreateEpisodeStyle(settings, height, SKTextAlign.Left);
            using var titleStyle = CreateTitleStyle(settings, height, SKTextAlign.Left);

            var consumed = BuildTextColumn(safeArea, settings, height, episodeMetadata, episodeStyle, titleStyle).Consumed;

            return new SKRect(
                safeArea.Left,
                safeArea.Bottom - consumed,
                safeArea.Left + (safeArea.Width * TextWidthRatio),
                safeArea.Bottom);
        }

        // RenderTypography
        // Renders the episode code and title in the bottom left corner of the poster.
        protected override void RenderTypography(SKCanvas skCanvas, EpisodeMetadata episodeMetadata, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(episodeMetadata);
            ArgumentNullException.ThrowIfNull(settings);

            var safeArea = GetSafeAreaBounds(width, height, settings);

            using var episodeStyle = CreateEpisodeStyle(settings, height, SKTextAlign.Left);
            using var titleStyle = CreateTitleStyle(settings, height, SKTextAlign.Left);

            var column = BuildTextColumn(safeArea, settings, height, episodeMetadata, episodeStyle, titleStyle);

            if (column.TryGetSlot(EpisodeBlock, out var codeSlot))
            {
                var episodeCode = EpisodeCodeUtils.FormatEpisodeCode(
                    episodeMetadata.SeasonNumber ?? 0,
                    episodeMetadata.EpisodeNumberStart ?? 0);
                episodeStyle.Draw(skCanvas, episodeCode, safeArea.Left, episodeStyle.BaselineAtBottom(codeSlot));
            }

            if (column.TryGetSlot(TitleBlock, out var titleSlot))
            {
                DrawTitleInSlot(skCanvas, episodeMetadata.EpisodeName!, titleStyle, titleSlot, safeArea.Left, safeArea.Width * TextWidthRatio, settings.LongTitleHandling);
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
