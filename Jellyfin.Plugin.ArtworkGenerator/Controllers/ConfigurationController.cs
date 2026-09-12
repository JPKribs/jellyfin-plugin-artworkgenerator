using System;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.ArtworkGenerator.Configuration;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services.Posters;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtworkGenerator.Controllers
{
    [ApiController]
    [Authorize(Policy = "RequiresElevation")]
    [Route("Plugins/ArtworkGenerator")]
    public class ConfigurationController : ControllerBase
    {
        // Cache reflection results — PluginConfiguration properties don't change at runtime
        private static readonly PropertyInfo[] ConfigProperties = typeof(PluginConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .ToArray();

        private readonly ILogger<ConfigurationController> _logger;

        public ConfigurationController(ILogger<ConfigurationController> logger)
        {
            _logger = logger;
        }

        // MARK: GET
        [HttpGet("Configuration")]
        public IActionResult GetConfiguration()
        {
            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                _logger.LogError("Plugin instance was null in GET.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Plugin not initialized.");
            }

            return Ok(plugin.Configuration);
        }

        // MARK: Logos
        // Logo designs live in their own file beside the configuration, so they are read and written
        // through their own endpoints rather than as part of the configuration payload.
        [HttpGet("Logos")]
        public IActionResult GetLogos()
        {
            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                _logger.LogError("Plugin instance was null in GET Logos.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Plugin not initialized.");
            }

            return Ok(plugin.PosterConfigService.GetLogoDesigns());
        }

        // MARK: POST
        // MARK: Logos
        [HttpPost("Logos")]
        public IActionResult SaveLogos([FromBody] LogoConfiguration[] logos)
        {
            if (logos == null || logos.Length == 0)
            {
                return BadRequest(new { success = false, error = "At least one logo design is required." });
            }

            try
            {
                var plugin = Plugin.Instance;
                if (plugin == null)
                {
                    _logger.LogError("Plugin instance was null in POST Logos.");
                    return StatusCode(StatusCodes.Status500InternalServerError, "Plugin not initialized.");
                }

                plugin.PosterConfigService.SaveLogoDesigns(plugin.Configuration, logos);
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save logo designs.");
                return BadRequest(new { success = false, error = ex.Message });
            }
        }

        // MARK: PosterStyles
        // Returns each poster style, its description, and the shapes it can lay out, read from the
        // generators themselves so the UI never hardcodes them.
        [HttpGet("PosterStyles")]
        public IActionResult GetPosterStyles()
        {
            var styles = PreviewService.GetStyleCatalog()
                .Select(g => new
                {
                    value = g.Style.ToString(),
                    description = g.Description,
                    portrait = g.Supports(ArtworkShape.Portrait),
                    landscape = g.Supports(ArtworkShape.Landscape),
                    settings = g.SettingRules.ToDictionary(rule => rule.Key, rule => rule.Value.ToString())
                });
            return Ok(styles);
        }

        // MARK: SettingOptions
        // Returns the choices and the starting value for every setting, read from the settings
        // themselves, so the configuration page never keeps its own copy of an enum or a default.
        [HttpGet("SettingOptions")]
        public IActionResult GetSettingOptions()
        {
            return Ok(new
            {
                options = SettingOptions.All(),
                defaults = SettingOptions.Defaults()
            });
        }

        // MARK: Fonts
        // Returns the font families actually installed on the server. The configuration page uses
        // this instead of a hardcoded list, which on a container image would offer fonts that are
        // not present and silently fall back to the Skia default.
        [HttpGet("Fonts")]
        public IActionResult GetFonts()
        {
            return Ok(FontUtils.GetAvailableFontFamilies());
        }

        // MARK: POST
        [HttpPost("Configuration")]
        public IActionResult UpdateConfiguration([FromBody] PluginConfiguration newConfig)
        {
            if (newConfig == null)
            {
                return BadRequest(new { success = false, error = "Configuration payload is required." });
            }

            try
            {
                var plugin = Plugin.Instance;
                if (plugin == null)
                {
                    _logger.LogError("Plugin instance was null in POST.");
                    return StatusCode(StatusCodes.Status500InternalServerError, "Plugin not initialized.");
                }

                // Debug rather than Information: this fires on every save and expands the whole
                // configuration into the server log.
                _logger.LogDebug("Received config: {@NewConfig}", newConfig);

                var currentConfig = plugin.Configuration;
                CopyConfigurationProperties(newConfig, currentConfig);

                plugin.SaveConfiguration();

                _logger.LogInformation("Configuration saved successfully.");
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update configuration.");
                return BadRequest(new { success = false, error = ex.Message });
            }
        }

        // MARK: Preview
        // Renders a design against the sample artwork at one shape. Every design renders both
        // shapes, so the Designs page asks for each. The item kind decides which text is shown.
        [HttpPost("Preview")]
        public IActionResult GeneratePreview(
            [FromBody] PosterSettings settings,
            [FromQuery] ArtworkItemKind kind = ArtworkItemKind.Series,
            [FromQuery] ArtworkShape shape = ArtworkShape.Landscape)
        {
            if (settings == null)
            {
                return BadRequest("Poster settings are required.");
            }

            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                _logger.LogError("Plugin instance was null in Preview.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Plugin not initialized.");
            }

            try
            {
                var imageBytes = plugin.PreviewService.GeneratePreview(settings, kind, shape);
                if (imageBytes == null)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, "Failed to render preview.");
                }

                return File(imageBytes, "image/jpeg");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate poster preview.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to render preview.");
            }
        }

        // MARK: Preview/Logo
        // Renders a logo design as a transparent PNG, for the sample series or a sample name.
        [HttpPost("Preview/Logo")]
        public IActionResult GenerateLogoPreview([FromBody] LogoSettings settings, [FromQuery] string? name = null)
        {
            if (settings == null)
            {
                return BadRequest("Logo settings are required.");
            }

            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                _logger.LogError("Plugin instance was null in logo Preview.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Plugin not initialized.");
            }

            try
            {
                var imageBytes = plugin.PreviewService.GenerateLogoPreview(settings, name?.Length > 200 ? name[..200] : name);
                if (imageBytes == null)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, "Failed to render logo preview.");
                }

                return File(imageBytes, "image/png");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate logo preview.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to render logo preview.");
            }
        }

        // MARK: PreviewComponent
        [HttpGet("Preview/Component/{component}")]
        public IActionResult GetPreviewComponent([FromRoute] string component)
        {
            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                _logger.LogError("Plugin instance was null in PreviewComponent.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Plugin not initialized.");
            }

            var result = plugin.PreviewService.GetComponentImage(component);
            if (result == null)
            {
                return NotFound();
            }

            return File(result.Value.Bytes, result.Value.ContentType);
        }

        // MARK: Generated
        // Serves an image already rendered for the Edit Images picker.
        //
        // Anonymous by necessity: Jellyfin fetches picker thumbnails and downloads the chosen
        // image using the server's own HTTP client, which sends no user credentials. Nothing is
        // generated here — the handler only resolves an unguessable, expiring token minted during
        // an authenticated provider call — so an unauthenticated caller has no work to trigger and
        // nothing to enumerate.
        [HttpGet("Generated/{token}")]
        [AllowAnonymous]
        public IActionResult GetGeneratedImage([FromRoute] string token)
        {
            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "Plugin not initialized.");
            }

            if (!plugin.GeneratedImageCache.TryGet(token, out var imageBytes, out var contentType))
            {
                return NotFound();
            }

            return File(imageBytes, contentType);
        }

        // MARK: CopyConfigurationProperties
        // Copies every settable property across. A failure here means the saved configuration
        // would silently differ from what the user submitted, so it aborts the save rather than
        // reporting success on a partial write.
        private void CopyConfigurationProperties(PluginConfiguration source, PluginConfiguration target)
        {
            foreach (var property in ConfigProperties)
            {
                try
                {
                    property.SetValue(target, property.GetValue(source));
                }
                catch (Exception ex)
                {
                    var sanitizedName = property.Name.Replace(Environment.NewLine, string.Empty, StringComparison.Ordinal);
                    _logger.LogError(ex, "Failed to copy property {PropertyName}", sanitizedName);
                    throw new InvalidOperationException($"Could not apply configuration property '{sanitizedName}'.", ex);
                }
            }
        }
    }
}
