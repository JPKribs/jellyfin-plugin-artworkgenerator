using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Services.Artwork;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Services.Posters
{
    /// <summary>
    /// Renders artwork against bundled demo art so the configuration UI can show a live preview of
    /// the current settings. Runs the same crop and generator pipeline the image providers use, but
    /// sourced from embedded sample images, so it needs no media, no IMediaEncoder, and no library.
    /// </summary>
    public class PreviewService
    {
        // Fixed sample metadata so previews read like a real item regardless of library state.
        private const string ShowName = "TV Show";
        private const string EpisodeName = "Episode Name";
        private const int SeasonNumber = 12;
        private const int EpisodeNumber = 7;
        private const int SeasonEpisodeCount = 10;
        private const int SeasonCount = 12;
        private const int ProductionYear = 2024;

        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<PreviewService> _logger;
        private readonly CroppingService _croppingService;
        private readonly LogoRenderer _logoRenderer;
        private readonly string _assetRoot;
        private readonly object _assetLock = new object();
        private volatile string? _assetDir;

        public PreviewService(ILoggerFactory loggerFactory, IApplicationPaths applicationPaths)
            : this(loggerFactory, Path.Combine(applicationPaths?.DataPath ?? throw new ArgumentNullException(nameof(applicationPaths)), "episodeposter", "preview-assets"))
        {
            // Kept under the plugin's own data directory rather than the shared system temp
            // directory: on a multi-user host /tmp is world-writable, so a fixed path there
            // could be pre-created by another local user and have its files replaced with
            // symlinks that this process would then follow.
        }

        /// <summary>
        /// Test/offline constructor that materializes demo assets under the given directory.
        /// </summary>
        public PreviewService(ILoggerFactory loggerFactory, string assetRoot)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);

            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<PreviewService>();
            _croppingService = new CroppingService(_loggerFactory.CreateLogger<CroppingService>());
            _logoRenderer = new LogoRenderer(_loggerFactory.CreateLogger<LogoRenderer>());
            _assetRoot = assetRoot;
        }

        // GeneratePreview
        // Renders a design against the demo artwork and returns JPEG bytes, or null on failure. The
        // design's shape decides the crop; the item kind decides the text.
        public byte[]? GeneratePreview(PosterSettings settings, ArtworkItemKind? kind = null)
        {
            ArgumentNullException.ThrowIfNull(settings);

            var assetDir = EnsureAssetsExtracted();
            var basePath = Path.Combine(assetDir, "demo-base.png");

            using var baseImage = SKBitmap.Decode(basePath);
            if (baseImage == null)
            {
                _logger.LogError("Failed to decode preview base image at {Path}", basePath);
                return null;
            }

            var subject = CreateDemoSubject(kind ?? DefaultKindFor(settings.Shape), assetDir, baseImage.Width, baseImage.Height);

            var bytes = settings.CanvasSource == CanvasSource.None
                ? RenderTransparentPoster(baseImage.Width, baseImage.Height, subject, settings)
                : RenderPoster(baseImage, subject, settings);

            if (bytes == null)
            {
                _logger.LogWarning("Preview generation returned no output for style {Style}", settings.PosterStyle);
            }

            return bytes;
        }

        // GenerateLogoPreview
        // Renders a logo design for the demo series as PNG bytes.
        public byte[]? GenerateLogoPreview(LogoSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            var assetDir = EnsureAssetsExtracted();
            var subject = CreateDemoSubject(ArtworkItemKind.Series, assetDir, 1920, 1080);
            return _logoRenderer.Render(subject, settings);
        }

        // RenderPoster
        // Shared render path: adjusts the design for its shape, crops the base image, selects the
        // style's generator, and returns the encoded poster. Both the live preview and the offline
        // demo image generator call it.
        public byte[]? RenderPoster(SKBitmap baseImage, ArtworkSubject subject, PosterSettings settings)
        {
            ArgumentNullException.ThrowIfNull(baseImage);
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var adjusted = ArtworkService.ShapeAdjust(settings, settings.Shape);
            var canvas = _croppingService.CropPoster(baseImage, adjusted);
            try
            {
                subject.VideoMetadata.VideoWidth = canvas.Width;
                subject.VideoMetadata.VideoHeight = canvas.Height;

                return CreateGeneratorFor(adjusted).Generate(canvas, subject, adjusted);
            }
            finally
            {
                if (!ReferenceEquals(canvas, baseImage))
                {
                    canvas.Dispose();
                }
            }
        }

        // RenderTransparentPoster
        // Mirrors the runtime CanvasSource.None path: renders the style over a blank transparent
        // canvas in the design's shape (no crop, since cropping a transparent bitmap would trip
        // letterbox detection).
        private byte[]? RenderTransparentPoster(int width, int height, ArtworkSubject subject, PosterSettings settings)
        {
            var adjusted = ArtworkService.ShapeAdjust(settings, settings.Shape);
            if (adjusted.Shape == ArtworkShape.Portrait)
            {
                width = height * 2 / 3;
            }

            using var canvas = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var skCanvas = new SKCanvas(canvas))
            {
                skCanvas.Clear(SKColors.Transparent);
            }

            return CreateGeneratorFor(adjusted).Generate(canvas, subject, adjusted);
        }

        // CreateGeneratorFor
        // The design's style, or Standard when the style cannot lay out the design's shape.
        private IPosterGenerator CreateGeneratorFor(PosterSettings settings)
        {
            var generator = CreateGenerator(settings.PosterStyle, _loggerFactory);
            return generator.Supports(settings.Shape)
                ? generator
                : CreateGenerator(PosterStyle.Standard, _loggerFactory);
        }

        // CreateGenerator
        // Maps a poster style to its generator implementation. Single source of truth for style
        // selection, shared by the preview, the demos, and the runtime artwork service.
        public static IPosterGenerator CreateGenerator(PosterStyle style, ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);

            return style switch
            {
                PosterStyle.Logo => new LogoPosterGenerator(loggerFactory.CreateLogger<LogoPosterGenerator>()),
                PosterStyle.Numeral => new NumeralPosterGenerator(loggerFactory.CreateLogger<NumeralPosterGenerator>()),
                PosterStyle.Cutout => new CutoutPosterGenerator(loggerFactory.CreateLogger<CutoutPosterGenerator>()),
                PosterStyle.Standard => new StandardPosterGenerator(loggerFactory.CreateLogger<StandardPosterGenerator>()),
                PosterStyle.Frame => new FramePosterGenerator(loggerFactory.CreateLogger<FramePosterGenerator>()),
                PosterStyle.Brush => new BrushPosterGenerator(loggerFactory.CreateLogger<BrushPosterGenerator>()),
                PosterStyle.Split => new SplitPosterGenerator(loggerFactory.CreateLogger<SplitPosterGenerator>()),
                PosterStyle.FrostedGlass => new FrostedGlassPosterGenerator(loggerFactory.CreateLogger<FrostedGlassPosterGenerator>()),
                PosterStyle.Fade => new FadePosterGenerator(loggerFactory.CreateLogger<FadePosterGenerator>()),
                PosterStyle.Timeline => new TimelinePosterGenerator(loggerFactory.CreateLogger<TimelinePosterGenerator>()),
                PosterStyle.Striped => new StripedPosterGenerator(loggerFactory.CreateLogger<StripedPosterGenerator>()),
                _ => new StandardPosterGenerator(loggerFactory.CreateLogger<StandardPosterGenerator>())
            };
        }

        // GetStyleCatalog
        // Returns one generator per poster style so the configuration UI can read each style's own
        // description and shapes instead of keeping a duplicate copy.
        public static IReadOnlyList<IPosterGenerator> GetStyleCatalog()
        {
            return Enum.GetValues<PosterStyle>()
                .Select(style => CreateGenerator(style, NullLoggerFactory.Instance))
                .ToList();
        }

        // GetComponentImage
        // Returns the raw bytes (and content type) of a single demo input component so the config
        // UI can show the user what feeds the preview. Returns null for an unknown component.
        public (byte[] Bytes, string ContentType)? GetComponentImage(string component)
        {
            var fileName = component switch
            {
                "canvas" => "demo-base.png",
                "poster" => "demo-poster.jpg",
                "logo" => "demo-logo.png",
                "graphic" => "demo-graphic.png",
                _ => null
            };

            if (fileName == null)
            {
                return null;
            }

            var resource = $"{typeof(Plugin).Namespace}.Assets.Demo.{fileName}";
            using var stream = typeof(PreviewService).Assembly.GetManifestResourceStream(resource);
            if (stream == null)
            {
                _logger.LogWarning("Embedded preview component not found: {Resource}", resource);
                return null;
            }

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var contentType = fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : "image/png";
            return (ms.ToArray(), contentType);
        }

        // GetDemoAssetDirectory
        // Extracts (once) and returns the directory holding the bundled demo artwork. This is the
        // single source of the demo art; the offline example generator reads from here too.
        public string GetDemoAssetDirectory()
        {
            return EnsureAssetsExtracted();
        }

        // DefaultKindFor
        // Portrait designs are mostly for series and season posters; landscape for episodes.
        private static ArtworkItemKind DefaultKindFor(ArtworkShape shape)
        {
            return shape == ArtworkShape.Portrait ? ArtworkItemKind.Season : ArtworkItemKind.Episode;
        }

        // CreateDemoSubject
        // A sample item of the given kind, pointing at the bundled art.
        private static ArtworkSubject CreateDemoSubject(ArtworkItemKind kind, string assetDir, int width, int height)
        {
            var videoMetadata = new VideoMetadata
            {
                VideoWidth = width,
                VideoHeight = height,
                SeriesLogoFilePath = Path.Combine(assetDir, "demo-logo.png"),
                SeriesPosterFilePath = Path.Combine(assetDir, "demo-poster.jpg"),
                SeriesBackdropFilePath = Path.Combine(assetDir, "demo-base.png")
            };

            return new ArtworkSubject(videoMetadata)
            {
                Kind = kind,
                SeriesName = ShowName,
                OriginalTitle = "Série Télé",
                SortTitle = ShowName,
                FolderName = "TV Show (2024) [tvdbid-000000]",
                ProductionYear = ProductionYear,
                SeasonNumber = SeasonNumber,
                SeasonName = "Season 12",
                SeasonCount = SeasonCount,
                EpisodeName = EpisodeName,
                EpisodeNumberStart = EpisodeNumber,
                EpisodeNumberEnd = EpisodeNumber,
                SeasonEpisodeCount = SeasonEpisodeCount
            };
        }

        // EnsureAssetsExtracted
        // Materializes the embedded demo artwork to disk once, since the crop and generator
        // pipeline reads logo/poster/graphic art from file paths rather than streams.
        private string EnsureAssetsExtracted()
        {
            var cached = _assetDir;
            if (cached != null)
            {
                return cached;
            }

            lock (_assetLock)
            {
                if (_assetDir != null)
                {
                    return _assetDir;
                }

                Directory.CreateDirectory(_assetRoot);

                ExtractAsset("demo-base.png", _assetRoot);
                ExtractAsset("demo-logo.png", _assetRoot);
                ExtractAsset("demo-graphic.png", _assetRoot);
                ExtractAsset("demo-poster.jpg", _assetRoot);

                _assetDir = _assetRoot;
                return _assetRoot;
            }
        }

        // ExtractAsset
        // Copies a single embedded demo asset to disk, overwriting any stale copy from a prior version.
        private void ExtractAsset(string fileName, string dir)
        {
            var resource = $"{typeof(Plugin).Namespace}.Assets.Demo.{fileName}";
            using var stream = typeof(PreviewService).Assembly.GetManifestResourceStream(resource);
            if (stream == null)
            {
                _logger.LogWarning("Embedded preview asset not found: {Resource}", resource);
                return;
            }

            var dest = Path.Combine(dir, fileName);
            using var file = File.Create(dest);
            stream.CopyTo(file);
        }
    }
}
