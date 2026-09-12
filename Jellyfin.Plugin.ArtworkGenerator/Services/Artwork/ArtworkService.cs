using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services.Posters;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Artwork
{
    /// <summary>
    /// Generates every kind of artwork the plugin offers: resolves the item's profile, picks the
    /// design for the requested slot, and hands off to the poster, backdrop, or logo pipeline.
    /// </summary>
    public class ArtworkService
    {
        /// <summary>
        /// Upper bound on how many frames a single request may ask for. Each distinct frame costs an
        /// extraction, and a poster slot draws every frame once per design, so the Edit Images
        /// picker can receive up to three times this many posters for one slot.
        /// </summary>
        public const int MaxCandidates = 10;

        /// <summary>
        /// The most a portrait render may enlarge a design's text. A very tall crop would otherwise
        /// scale it past what the frame can hold.
        /// </summary>
        private const float MaxPortraitTextScale = 1.6f;

        private readonly ILogger<ArtworkService> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly CanvasService _canvasService;
        private readonly LogoRenderer _logoRenderer;
        private readonly PosterConfigurationService _configService;

        public ArtworkService(
            ILogger<ArtworkService> logger,
            ILoggerFactory loggerFactory,
            CanvasService canvasService,
            LogoRenderer logoRenderer,
            PosterConfigurationService configService)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
            _canvasService = canvasService;
            _logoRenderer = logoRenderer;
            _configService = configService;
        }

        /// <summary>Returns the artwork kind for a library item, or null when it is not supported.</summary>
        public static ArtworkItemKind? GetKind(BaseItem item) => item switch
        {
            Episode => ArtworkItemKind.Episode,
            Season => ArtworkItemKind.Season,
            Series => ArtworkItemKind.Series,
            Movie => ArtworkItemKind.Movie,

            // Last, because Episode, Movie and MusicVideo all derive from Video: anything still
            // unclaimed here is a video that stands on its own.
            Video => ArtworkItemKind.Video,
            _ => null
        };

        /// <summary>Returns the slot for a Jellyfin image type, or null when the plugin never fills it.</summary>
        public static ArtworkSlot? GetSlot(ImageType type) => type switch
        {
            ImageType.Primary => ArtworkSlot.Primary,
            ImageType.Thumb => ArtworkSlot.Thumb,
            ImageType.Logo => ArtworkSlot.Logo,
            ImageType.Backdrop => ArtworkSlot.Backdrop,
            _ => null
        };

        /// <summary>Returns the Jellyfin image type a slot fills.</summary>
        public static ImageType ToImageType(ArtworkSlot slot) => slot switch
        {
            ArtworkSlot.Thumb => ImageType.Thumb,
            ArtworkSlot.Logo => ImageType.Logo,
            ArtworkSlot.Backdrop => ImageType.Backdrop,
            _ => ImageType.Primary
        };

        /// <summary>
        /// Adjusts a design for the shape it is rendering at. Every design renders both shapes: a
        /// portrait takes the design's portrait ratio and text scale, and always crops, since a
        /// portrait cut from a landscape frame cannot be stretched or left wide. A landscape render
        /// never takes a portrait ratio.
        /// </summary>
        public static PosterSettings ShapeAdjust(PosterSettings design, ArtworkShape shape)
        {
            ArgumentNullException.ThrowIfNull(design);

            var settings = design.Clone();
            settings.Shape = shape;

            // Frame extraction belongs to the server, not to the design, so whatever a saved design
            // or an imported template still carries is replaced here before anything is drawn.
            BasePosterGenerator.WithFrameExtraction(settings, Plugin.Instance?.Configuration?.FrameExtraction);

            var ratio = CroppingService.ParseAspectRatio(settings.PosterDimensionRatio);
            if (shape == ArtworkShape.Portrait)
            {
                var portrait = CroppingService.ParseAspectRatio(settings.PortraitDimensionRatio);
                if (portrait > 0f && portrait < 1f)
                {
                    settings.PosterDimensionRatio = settings.PortraitDimensionRatio;
                }
                else if (ratio > 0f && ratio < 1f)
                {
                    portrait = ratio;
                }
                else
                {
                    settings.PosterDimensionRatio = "2:3";
                    portrait = 2f / 3f;
                }

                // Every size is a percent of the short side, which in portrait is the width, so the
                // same percent draws text that looks small against a tall frame. Measuring against
                // the average of the two sides instead gives the text the same weight in both
                // shapes: a 2:3 poster takes about a quarter more than its width alone would give.
                var textScale = Math.Clamp(MathF.Sqrt(1f / portrait), 1f, MaxPortraitTextScale);
                settings.PrimaryFontSize *= textScale;
                settings.SecondaryFontSize *= textScale;
                settings.PosterFill = PosterFill.Fit;
            }
            else if (ratio < 1f)
            {
                settings.PosterDimensionRatio = "16:9";
            }

            return settings;
        }

        /// <summary>
        /// Tells the subject whether the design draws a primary line, which decides whether an item
        /// with no name of its own can promote its subtitle into it. That is a question about the
        /// design, not the item.
        /// </summary>
        /// <param name="subject">The item being drawn.</param>
        /// <param name="settings">The design drawing it.</param>
        public static void ApplySubjectRules(ArtworkSubject subject, PosterSettings settings)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            subject.PrimaryShown = settings.ShowPrimary;
        }

        /// <summary>
        /// Returns the image types the item's profile has turned on, in slot order.
        /// </summary>
        public IReadOnlyList<ImageType> GetEnabledImageTypes(BaseItem item)
        {
            ArgumentNullException.ThrowIfNull(item);

            var kind = GetKind(item);
            if (kind == null)
            {
                return Array.Empty<ImageType>();
            }

            var profile = _configService.GetProfileFor(kind.Value, ProfileKey(item, kind.Value));
            return ArtworkProfile.SupportedSlots
                .Where(s => s.Kind == kind.Value)
                .Select(s => profile.GetSlot(s.Kind, s.Slot))
                .Where(assignment => assignment is { Enabled: true })
                .Select(assignment => ToImageType(assignment!.Slot))
                .ToList();
        }

        /// <summary>
        /// Generates up to <paramref name="count"/> images of the requested type for an item. Returns
        /// nothing when the item's profile does not fill that slot.
        /// </summary>
        /// <param name="item">The item to draw.</param>
        /// <param name="type">The image type to draw.</param>
        /// <param name="count">How many frames to draw.</param>
        /// <param name="includeAlternateDesigns">
        /// Whether a poster slot's secondary and tertiary designs also draw each frame, for the Edit
        /// Images picker. An automatic refresh keeps one image, so it draws the first design only.
        /// </param>
        /// <param name="cancellationToken">Cancels the render.</param>
        public async Task<IReadOnlyList<GeneratedArtwork>> GenerateAsync(
            BaseItem item,
            ImageType type,
            int count,
            bool includeAlternateDesigns,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(item);

            var kind = GetKind(item);
            var slot = GetSlot(type);
            if (kind == null || slot == null || !ArtworkProfile.IsSupported(kind.Value, slot.Value))
            {
                return Array.Empty<GeneratedArtwork>();
            }

            var profile = _configService.GetProfileFor(kind.Value, ProfileKey(item, kind.Value));
            var assignment = profile.GetSlot(kind.Value, slot.Value);
            if (assignment is not { Enabled: true })
            {
                return Array.Empty<GeneratedArtwork>();
            }

            count = Math.Clamp(count, 1, MaxCandidates);
            var subject = ArtworkSubject.FromItem(item);

            _logger.LogInformation(
                "Generating {Count} {Slot} image(s) for {Kind} {Name} using profile {Profile}",
                count,
                slot.Value,
                kind.Value,
                DescribeItem(item),
                profile.Name);

            return slot.Value switch
            {
                ArtworkSlot.Logo => await RenderLogosAsync(item, subject, profile, assignment, count, cancellationToken).ConfigureAwait(false),
                ArtworkSlot.Backdrop => await RenderBackdropsAsync(item, subject, profile, count, cancellationToken).ConfigureAwait(false),
                _ => await RenderPostersAsync(item, subject, profile, kind.Value, slot.Value, assignment, count, includeAlternateDesigns, cancellationToken).ConfigureAwait(false)
            };
        }

        // RenderPostersAsync
        // Primary and Thumb: the assigned design, adjusted to the slot's shape, drawn over canvases
        // from the item's frame pool. With alternates, the slot's secondary and tertiary designs
        // draw the same frame ranks too, and the results are interleaved by frame so the picker
        // shows every design's take on one frame side by side.
        private async Task<IReadOnlyList<GeneratedArtwork>> RenderPostersAsync(
            BaseItem item,
            ArtworkSubject subject,
            ArtworkProfile profile,
            ArtworkItemKind kind,
            ArtworkSlot slot,
            SlotAssignment assignment,
            int count,
            bool includeAlternateDesigns,
            CancellationToken cancellationToken)
        {
            var shape = profile.GetShape(kind, slot);
            var designs = includeAlternateDesigns
                ? _configService.GetDesignsForSlot(assignment)
                : new[] { _configService.GetDesignForSlot(assignment) };

            // Each design takes its frames from the pool on its own. Holding the pool for the whole
            // render keeps it from being discarded between designs while other items' pools crowd
            // it, which would hand the later designs a new pool with different frames and extract
            // them all over again.
            using var hold = designs.Count > 1
                ? await _canvasService.HoldFramePoolAsync(item, ShapeAdjust(designs[0], shape), cancellationToken).ConfigureAwait(false)
                : null;

            var perDesign = new List<IReadOnlyList<GeneratedArtwork?>>(designs.Count);
            foreach (var design in designs)
            {
                perDesign.Add(await RenderDesignAsync(item, subject, ShapeAdjust(design, shape), shape, slot, count, cancellationToken).ConfigureAwait(false));
            }

            return InterleaveByFrame(perDesign);
        }

        // RenderDesignAsync
        // One design over the slot's frame ranks. Each entry is one frame, null where that frame
        // failed to render, so a failure never shifts the frames that follow out of line with the
        // other designs'. Canvases are released before the next design starts.
        private async Task<IReadOnlyList<GeneratedArtwork?>> RenderDesignAsync(
            BaseItem item,
            ArtworkSubject subject,
            PosterSettings settings,
            ArtworkShape shape,
            ArtworkSlot slot,
            int count,
            CancellationToken cancellationToken)
        {
            var generator = CreateGenerator(settings.PosterStyle, shape);

            ApplySubjectRules(subject, settings);

            var canvases = await _canvasService
                .GenerateCanvasesAsync(item, subject, settings, shape, RankOffset(slot), count, cancellationToken)
                .ConfigureAwait(false);

            var results = new GeneratedArtwork?[canvases.Count];
            try
            {
                for (int i = 0; i < canvases.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var canvas = canvases[i];
                    var bytes = generator.Generate(canvas, subject, settings);
                    if (bytes == null)
                    {
                        _logger.LogWarning("Rendering failed for {Name}", DescribeItem(item));
                        continue;
                    }

                    results[i] = new GeneratedArtwork(bytes, "image/jpeg", ImageFormat.Jpg, canvas.Width, canvas.Height);
                }
            }
            finally
            {
                foreach (var canvas in canvases)
                {
                    canvas.Dispose();
                }
            }

            return results;
        }

        // InterleaveByFrame
        // Frame by frame, each design's image of that frame in design order. A design with fewer
        // frames, such as one drawn over the series backdrop, simply runs out early.
        internal static IReadOnlyList<GeneratedArtwork> InterleaveByFrame(IReadOnlyList<IReadOnlyList<GeneratedArtwork?>> perDesign)
        {
            var frames = perDesign.Count == 0 ? 0 : perDesign.Max(images => images.Count);
            var results = new List<GeneratedArtwork>();

            for (int frame = 0; frame < frames; frame++)
            {
                foreach (var images in perDesign)
                {
                    if (frame < images.Count && images[frame] is { } artwork)
                    {
                        results.Add(artwork);
                    }
                }
            }

            return results;
        }

        // RenderBackdropsAsync
        // Backdrop: a frame from the item's pool cropped to the backdrop ratio, with no design.
        private async Task<IReadOnlyList<GeneratedArtwork>> RenderBackdropsAsync(
            BaseItem item,
            ArtworkSubject subject,
            ArtworkProfile profile,
            int count,
            CancellationToken cancellationToken)
        {
            var canvases = await _canvasService
                .GenerateBackdropCanvasesAsync(item, subject, profile.Backdrop ?? new BackdropSettings(), RankOffset(ArtworkSlot.Backdrop), count, cancellationToken)
                .ConfigureAwait(false);

            var results = new List<GeneratedArtwork>(canvases.Count);
            try
            {
                foreach (var canvas in canvases)
                {
                    using var image = SKImage.FromBitmap(canvas);
                    using var data = image.Encode(SKEncodedImageFormat.Jpeg, RenderConstants.JpegQuality);
                    if (data != null)
                    {
                        results.Add(new GeneratedArtwork(data.ToArray(), "image/jpeg", ImageFormat.Jpg, canvas.Width, canvas.Height));
                    }
                }
            }
            finally
            {
                foreach (var canvas in canvases)
                {
                    canvas.Dispose();
                }
            }

            return results;
        }

        // RenderLogosAsync
        // Logo: a colored logo is text only, so there is only ever one. A photo-filled logo takes
        // its picture from the item's frame pool, one frame per alternate, so each choice differs;
        // with no frames it uses the series backdrop, and with neither it falls back to the color.
        private async Task<IReadOnlyList<GeneratedArtwork>> RenderLogosAsync(
            BaseItem item,
            ArtworkSubject subject,
            ArtworkProfile profile,
            SlotAssignment assignment,
            int count,
            CancellationToken cancellationToken)
        {
            var settings = _configService.GetLogoForSlot(assignment);
            if (settings.Fill != LogoFill.Photo)
            {
                return RenderLogo(subject, settings, null);
            }

            var photos = await _canvasService
                .GenerateBackdropCanvasesAsync(item, subject, profile.Backdrop ?? new BackdropSettings(), RankOffset(ArtworkSlot.Logo), count, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                if (photos.Count == 0)
                {
                    using var backdrop = LoadSeriesBackdrop(subject);
                    return RenderLogo(subject, settings, backdrop);
                }

                var results = new List<GeneratedArtwork>(photos.Count);
                foreach (var photo in photos)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    results.AddRange(RenderLogo(subject, settings, photo));
                }

                return results;
            }
            finally
            {
                foreach (var photo in photos)
                {
                    photo.Dispose();
                }
            }
        }

        // LoadSeriesBackdrop
        // The series backdrop as a photo for a logo, or null when there is none.
        private static SKBitmap? LoadSeriesBackdrop(ArtworkSubject subject)
        {
            var path = subject.VideoMetadata.SeriesBackdropFilePath;
            return !string.IsNullOrEmpty(path) && File.Exists(path) ? SKBitmap.Decode(path) : null;
        }

        // RenderLogo
        // One logo, filled with the photo when there is one.
        private GeneratedArtwork[] RenderLogo(ArtworkSubject subject, LogoSettings settings, SKBitmap? photo)
        {
            var bytes = _logoRenderer.Render(subject, settings, photo);
            if (bytes == null)
            {
                return Array.Empty<GeneratedArtwork>();
            }

            return new[] { new GeneratedArtwork(bytes, "image/png", ImageFormat.Png, settings.Width, settings.Height) };
        }

        // CreateGenerator
        // The style's generator, or Standard when the style cannot lay out this shape.
        private IPosterGenerator CreateGenerator(PosterStyle style, ArtworkShape shape)
        {
            var generator = PreviewService.CreateGenerator(style, _loggerFactory);
            if (generator.Supports(shape))
            {
                return generator;
            }

            _logger.LogWarning("Poster style {Style} does not support {Shape} posters; using Standard instead", style, shape);
            return PreviewService.CreateGenerator(PosterStyle.Standard, _loggerFactory);
        }

        // RankOffset
        // Each slot starts at a different rank of the shared pool, so a series' poster, backdrop,
        // thumb, and photo logo show four different frames rather than the same one four times.
        private static int RankOffset(ArtworkSlot slot) => slot switch
        {
            ArtworkSlot.Backdrop => 1,
            ArtworkSlot.Thumb => 2,
            ArtworkSlot.Logo => 3,
            _ => 0
        };

        // ProfileKey
        // What a profile assignment is keyed on: a film is assigned by its own id, everything else
        // by the series it belongs to.
        private static Guid ProfileKey(BaseItem item, ArtworkItemKind kind)
        {
            return kind.IsStandalone() ? item.Id : GetSeriesId(item);
        }

        private static Guid GetSeriesId(BaseItem item) => item switch
        {
            Episode episode => episode.SeriesId,
            Season season => season.SeriesId,
            Series series => series.Id,
            _ => Guid.Empty
        };

        private static string DescribeItem(BaseItem item) => item switch
        {
            Episode episode => episode.SeriesName + " - " + episode.Name,
            Season season => season.SeriesName + " - " + season.Name,
            _ => item.Name ?? string.Empty
        };
    }
}
