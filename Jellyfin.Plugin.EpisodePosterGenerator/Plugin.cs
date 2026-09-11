using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Jellyfin.Plugin.EpisodePosterGenerator.Configuration;
using Jellyfin.Plugin.EpisodePosterGenerator.Services;
using Jellyfin.Plugin.EpisodePosterGenerator.Services.Artwork;
using Jellyfin.Plugin.EpisodePosterGenerator.Services.Posters;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EpisodePosterGenerator
{
    [SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The plugin lives for the whole server process; the frame pool clears its own directory on the next start.")]
    public class Plugin : PluginBase<Plugin, PluginConfiguration>
    {
        public override string Name => "Episode Poster Generator";
        public override Guid Id => Guid.Parse("b8715e44-6b77-4c88-9c74-2b6f4c7b9a1e");
        public override string Description => "Generates posters, thumbs, logos, and backdrops for series, seasons, and episodes from their video.";

        private readonly ILogger<Plugin> _logger;
        private readonly PosterConfigurationService _posterConfigService;
        private readonly FramePoolService _framePool;
        private readonly ArtworkService _artworkService;
        private readonly PreviewService _previewService;
        private readonly GeneratedImageCache _generatedImageCache;

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

            _posterConfigService = new PosterConfigurationService(
                loggerFactory.CreateLogger<PosterConfigurationService>(),
                new LogoDesignStore(Path.Combine(applicationPaths.DataPath, "episodeposter", "logos.json")));
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
                Path.Combine(applicationPaths.DataPath, "episodeposter", "frame-pool"),
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

            _previewService = new PreviewService(loggerFactory, applicationPaths);
            _generatedImageCache = new GeneratedImageCache(
                loggerFactory.CreateLogger<GeneratedImageCache>());

            // Container images ship almost none of the common desktop fonts, so a configured
            // family often silently falls back to Skia's default. Surface it once per family.
            Utilities.FontUtils.SetMissingFamilyReporter(family =>
                _logger.LogWarning(
                    "Font family '{Family}' is not installed on this server; falling back to the default font. Install the font, or pick one offered by the configuration page.",
                    family));

            _logger.LogInformation("Episode Poster Generator plugin initialized");
        }

        public ArtworkService ArtworkService => _artworkService;

        public PosterConfigurationService PosterConfigService => _posterConfigService;

        public PreviewService PreviewService => _previewService;

        /// <summary>
        /// Gets the short-lived store backing the generated image URLs handed to Jellyfin's remote
        /// image picker.
        /// </summary>
        public GeneratedImageCache GeneratedImageCache => _generatedImageCache;

        // GetPages
        // Returns the plugin configuration pages. Only the first tab is listed in the dashboard menu.
        public override IEnumerable<PluginPageInfo> GetPages()
        {
            var ns = typeof(Plugin).Namespace;

            yield return new PluginPageInfo
            {
                Name = "epg_posters",
                EmbeddedResourcePath = $"{ns}.Configuration.epg_posters.html",
                MenuSection = "plugin",
                DisplayName = "Episode Poster Generator",
                MenuIcon = "image"
            };

            foreach (var name in new[] { "epg_posters.js", "epg_logos", "epg_logos.js", "epg_profiles", "epg_profiles.js", "epg_settings", "epg_settings.js", "epg_shared.css" })
            {
                var file = name.Contains('.', StringComparison.Ordinal) ? name : name + ".html";
                yield return new PluginPageInfo
                {
                    Name = name,
                    EmbeddedResourcePath = $"{ns}.Configuration.{file}"
                };
            }

            foreach (var page in GetSharedPages("epg"))
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
