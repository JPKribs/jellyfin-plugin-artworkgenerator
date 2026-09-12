using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Jellyfin.Plugin.ArtworkGenerator.Configuration;
using Jellyfin.Plugin.ArtworkGenerator.Services;
using Jellyfin.Plugin.ArtworkGenerator.Services.Artwork;
using Jellyfin.Plugin.ArtworkGenerator.Services.Posters;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtworkGenerator
{
    [SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The plugin lives for the whole server process; the frame pool clears its own directory on the next start, and the image cache sweep timer ends with the process.")]
    public class Plugin : PluginBase<Plugin, PluginConfiguration>
    {
        public override string Name => "Artwork Generator";
        public override Guid Id => Guid.Parse("b8715e44-6b77-4c88-9c74-2b6f4c7b9a1e");
        /// <summary>
        /// Gets the configuration file name, pinned to the plugin's former assembly name.
        /// Jellyfin derives this from the assembly, so renaming the project would otherwise make the
        /// server look for a file that does not exist and silently start every user from defaults.
        /// </summary>
        public override string ConfigurationFileName => "Jellyfin.Plugin.EpisodePosterGenerator.xml";

        public override string Description => "Generates posters, thumbs, logos, and backdrops for series, seasons, episodes, films, and other videos from their video.";

        private readonly ILogger<Plugin> _logger;
        private readonly PosterConfigurationService _posterConfigService;
        private readonly FramePoolService _framePool;
        private readonly ArtworkService _artworkService;
        private readonly PreviewService _previewService;
        private readonly GeneratedImageCache _generatedImageCache;
        private readonly GeneratedCandidateMemo _candidateMemo = new();

        // Plugin
        // Initializes the plugin with all required services and dependencies.
        public Plugin(
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            ILogger<Plugin> logger,
            ILoggerFactory loggerFactory,
            IMediaEncoder mediaEncoder)
            : base(applicationPaths, xmlSerializer)
        {
            _logger = logger;

            var dataRoot = Path.Combine(applicationPaths.DataPath, "artworkgenerator");
            _posterConfigService = new PosterConfigurationService(
                loggerFactory.CreateLogger<PosterConfigurationService>(),
                new LogoDesignStore(Path.Combine(dataRoot, "logos.json")));
            _posterConfigService.Initialize(Configuration);

            var brightnessService = new BrightnessService(
                loggerFactory.CreateLogger<BrightnessService>());
            var frameExtractionService = new FrameExtractionService(
                loggerFactory.CreateLogger<FrameExtractionService>(),
                mediaEncoder);
            var croppingService = new CroppingService(
                loggerFactory.CreateLogger<CroppingService>());

            _framePool = new FramePoolService(
                loggerFactory.CreateLogger<FramePoolService>(),
                frameExtractionService,
                Path.Combine(dataRoot, "frame-pool"),
                () => Configuration?.FixedExtractionSeed);

            var canvasService = new CanvasService(
                loggerFactory.CreateLogger<CanvasService>(),
                _framePool,
                croppingService,
                brightnessService);

            _artworkService = new ArtworkService(
                loggerFactory.CreateLogger<ArtworkService>(),
                loggerFactory,
                canvasService,
                new LogoRenderer(loggerFactory.CreateLogger<LogoRenderer>()),
                _posterConfigService);

            _previewService = new PreviewService(loggerFactory, Path.Combine(dataRoot, "preview-assets"));
            _generatedImageCache = new GeneratedImageCache(
                loggerFactory.CreateLogger<GeneratedImageCache>(),
                () => Configuration?.ImageCacheMinutes ?? GeneratedImageCache.DefaultLifetimeMinutes);

            // Container images ship almost none of the common desktop fonts, so a configured
            // family often silently falls back to Skia's default. Surface it once per family.
            Utilities.FontUtils.SetMissingFamilyReporter(family =>
                _logger.LogWarning(
                    "Font family '{Family}' is not installed on this server. Falling back to the default font. Install the font, or pick one offered by the configuration page.",
                    family));

            _logger.LogInformation("Artwork Generator plugin initialized");
        }

        public ArtworkService ArtworkService => _artworkService;

        public PosterConfigurationService PosterConfigService => _posterConfigService;

        public PreviewService PreviewService => _previewService;

        /// <summary>
        /// Gets the short-lived store backing the generated image URLs handed to Jellyfin's remote
        /// image picker.
        /// </summary>
        public GeneratedImageCache GeneratedImageCache => _generatedImageCache;

        /// <summary>
        /// Gets the choices last offered for each item in Edit Images, reused while they are still
        /// current so switching image type or page does not render everything again.
        /// </summary>
        public GeneratedCandidateMemo CandidateMemo => _candidateMemo;

        // GetPages
        // Returns the plugin configuration pages. Only the first tab is listed in the dashboard menu.
        public override IEnumerable<PluginPageInfo> GetPages()
        {
            var ns = typeof(Plugin).Namespace;

            yield return new PluginPageInfo
            {
                Name = "ag_posters",
                EmbeddedResourcePath = $"{ns}.Configuration.ag_posters.html",
                MenuSection = "plugin",
                DisplayName = "Artwork Generator",
                MenuIcon = "image"
            };

            foreach (var name in new[] { "ag_posters.js", "ag_logos", "ag_logos.js", "ag_profiles", "ag_profiles.js", "ag_settings", "ag_settings.js", "ag_shared.css" })
            {
                var file = name.Contains('.', StringComparison.Ordinal) ? name : name + ".html";
                yield return new PluginPageInfo
                {
                    Name = name,
                    EmbeddedResourcePath = $"{ns}.Configuration.{file}"
                };
            }

            foreach (var page in GetSharedPages("ag"))
            {
                yield return page;
            }
        }

        // UpdateConfiguration
        // Updates the configuration and reinitializes the configuration lookups.
        public override void UpdateConfiguration(BasePluginConfiguration configuration)
        {
            base.UpdateConfiguration(configuration);
            _posterConfigService?.Initialize(Configuration);
        }
    }
}
