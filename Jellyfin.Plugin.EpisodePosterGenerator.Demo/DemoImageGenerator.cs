using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Services;
using Jellyfin.Plugin.EpisodePosterGenerator.Services.Posters;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.EpisodePosterGenerator.DemoGenerator
{
    /// <summary>
    /// Generates demo poster images for all configuration templates using a public domain base image.
    /// </summary>
    public class DemoImageGenerator
    {
        private const string ExamplesDirectory = "../docs/examples";

        private const string ShowName = "TV Show";
        private const string EpisodeName = "Episode Name";
        private const int SeasonNumber = 12;

        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<DemoImageGenerator> _logger;
        private readonly PreviewService _previewService;

        // Demo artwork is sourced from the plugin's embedded assets (extracted by PreviewService),
        // so there is a single copy of the art shared with the live preview.
        private readonly string _baseImagePath;
        private readonly string _logoImagePath;
        private readonly string _graphicImagePath;
        private readonly string _seriesPosterPath;

        public DemoImageGenerator()
        {
            _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
            _logger = _loggerFactory.CreateLogger<DemoImageGenerator>();

            // Offline tool: no Jellyfin application paths available, so the demo art is
            // materialized under the local temp directory instead of the plugin data folder.
            _previewService = new PreviewService(
                _loggerFactory,
                Path.Combine(Path.GetTempPath(), "epg-demo-assets"));

            var assetDir = _previewService.GetDemoAssetDirectory();
            _baseImagePath = Path.Combine(assetDir, "demo-base.png");
            _logoImagePath = Path.Combine(assetDir, "demo-logo.png");
            _graphicImagePath = Path.Combine(assetDir, "demo-graphic.png");
            _seriesPosterPath = Path.Combine(assetDir, "demo-poster.jpg");
        }

        public async Task GenerateAllDemosAsync()
        {
            _logger.LogInformation("Starting demo image generation...");

            // Find all template files
            var templateFiles = Directory.GetFiles(ExamplesDirectory, "Template.json", SearchOption.AllDirectories);
            _logger.LogInformation($"Found {templateFiles.Length} template(s) to process");

            foreach (var templateFile in templateFiles)
            {
                try
                {
                    var templateDir = Path.GetDirectoryName(templateFile);
                    var exampleName = Path.GetFileName(templateDir);
                    _logger.LogInformation($"Processing template: {exampleName}");

                    // Ten landscape episodes, then the same design as the portrait posters a
                    // profile asks it for, since every design draws every kind.
                    for (int episodeNumber = 1; episodeNumber <= 10; episodeNumber++)
                    {
                        await GenerateDemoForTemplateAsync(templateFile, templateDir!, ArtworkItemKind.Episode, episodeNumber);
                        _logger.LogInformation($"  ✓ Generated Example{episodeNumber}.png");
                    }

                    await GenerateDemoForTemplateAsync(templateFile, templateDir!, ArtworkItemKind.Season, null);
                    _logger.LogInformation("  ✓ Generated Season.png");

                    await GenerateDemoForTemplateAsync(templateFile, templateDir!, ArtworkItemKind.Series, null);
                    _logger.LogInformation("  ✓ Generated Series.png");

                    _logger.LogInformation($"✓ Generated all demos for: {exampleName}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to generate demo for template: {templateFile}");
                }
            }

            _logger.LogInformation("Demo generation complete!");
        }

        // GenerateDemoForTemplateAsync
        // Renders one demo: a landscape episode, or the portrait poster a profile would ask this
        // design for on a season or a series.
        private async Task GenerateDemoForTemplateAsync(string templatePath, string templateDir, ArtworkItemKind kind, int? episodeNumber)
        {
            // Load template with case-insensitive property matching and enum converter
            var templateJson = await File.ReadAllTextAsync(templatePath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            };
            var template = JsonSerializer.Deserialize<PosterTemplate>(templateJson, options);

            if (template?.Settings == null)
            {
                throw new InvalidOperationException($"Invalid template file: {templatePath}");
            }

            var settings = template.Settings;

            // Load and prepare base image
            using var baseImage = SKBitmap.Decode(_baseImagePath);
            if (baseImage == null)
            {
                throw new FileNotFoundException($"Base image not found: {_baseImagePath}");
            }

            // Create mock video metadata for the CroppingService
            var videoMetadata = new VideoMetadata
            {
                VideoWidth = baseImage.Width,
                VideoHeight = baseImage.Height
            };

            // The series art every style may reach for: the Logo style draws the logo, Split the
            // poster, and any style can fall back to the backdrop.
            if (File.Exists(_logoImagePath))
            {
                videoMetadata.SeriesLogoFilePath = _logoImagePath;
            }

            if (File.Exists(_seriesPosterPath))
            {
                videoMetadata.SeriesPosterFilePath = _seriesPosterPath;
            }

            // For example generation WE supply the static graphic: if the template enables a
            // graphic, point it at the demo asset so the rendered examples show it. (The live
            // preview, by contrast, uses the user's own GraphicPath untouched.)
            if (!string.IsNullOrEmpty(settings.GraphicPath))
            {
                if (File.Exists(_graphicImagePath))
                {
                    settings.GraphicPath = _graphicImagePath;
                }
            }

            var metadata = kind switch
            {
                ArtworkItemKind.Episode => new ArtworkSubject
                {
                    Kind = ArtworkItemKind.Episode,
                    EpisodeName = EpisodeName,
                    SeriesName = ShowName,
                    SeasonNumber = SeasonNumber,
                    EpisodeNumberStart = episodeNumber ?? 1,
                    SeasonEpisodeCount = 10,
                    VideoMetadata = videoMetadata
                },

                // A plainly numbered season, the common case: its number carries it and the title
                // line stays empty rather than saying the same thing twice.
                ArtworkItemKind.Season => new ArtworkSubject
                {
                    Kind = ArtworkItemKind.Season,
                    SeriesName = ShowName,
                    SeasonNumber = SeasonNumber,
                    SeasonName = $"Season {SeasonNumber}",
                    SeasonCount = SeasonNumber,
                    SeasonEpisodeCount = 10,
                    VideoMetadata = videoMetadata
                },

                _ => new ArtworkSubject
                {
                    Kind = ArtworkItemKind.Series,
                    SeriesName = ShowName,
                    VideoMetadata = videoMetadata
                }
            };

            settings.Shape = kind == ArtworkItemKind.Episode ? ArtworkShape.Landscape : ArtworkShape.Portrait;

            // Render using the shared crop + generate pipeline (the same code path the live
            // preview and runtime use), so demos and previews can never drift apart.
            var imageBytes = _previewService.RenderPoster(baseImage, metadata, settings);

            if (imageBytes == null)
            {
                throw new InvalidOperationException($"Failed to generate the {kind} poster{(episodeNumber.HasValue ? " for episode " + episodeNumber.Value.ToString(CultureInfo.InvariantCulture) : string.Empty)}");
            }

            // The generator encodes JPEG; the .png extension is kept because every README and
            // docs page already links these filenames.
            var outputPath = Path.Combine(templateDir, kind switch
            {
                ArtworkItemKind.Episode => $"Example{episodeNumber ?? 1}.png",
                ArtworkItemKind.Season => "Season.png",
                _ => "Series.png"
            });
            await File.WriteAllBytesAsync(outputPath, imageBytes).ConfigureAwait(false);
        }

        /// <summary>
        /// Shape of the docs/examples/*/Template.json files. Local to this tool: the plugin
        /// itself does template import/export in the configuration page, not on the server.
        /// </summary>
        private sealed class PosterTemplate
        {
            public string? Name { get; set; }

            public string? Description { get; set; }

            public string? Author { get; set; }

            public string? Version { get; set; }

            public PosterSettings? Settings { get; set; }
        }
    }
}
