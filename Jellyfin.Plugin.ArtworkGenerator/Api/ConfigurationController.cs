using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Configuration;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services.Fonts;
using Jellyfin.Plugin.ArtworkGenerator.Services.Posters;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using JPKribs.Jellyfin.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtworkGenerator.Api
{
    [ApiController]
    [Authorize(Policy = "RequiresElevation")]
    [Route("Plugins/ArtworkGenerator")]
    public class ConfigurationController : ControllerBase
    {
        // A 20 MB font as base64, with room for the JSON around it.
        private const int MaxFontRequestBytes = 28 * 1024 * 1024;

        // A generated image never changes under its token, so a browser may keep it for a day,
        // the longest the server itself keeps one.
        internal const string GeneratedImageCacheControl = "private, max-age=86400, immutable";

        private readonly ILogger<ConfigurationController> _logger;

        public ConfigurationController(ILogger<ConfigurationController> logger)
        {
            _logger = logger;
        }

        // MARK: GET
        [HttpGet("Configuration")]
        public IActionResult GetConfiguration([FromQuery] string? language = null)
        {
            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                _logger.LogError("Plugin instance was null in GET.");
                return NotInitialized(language);
            }

            return Ok(plugin.Configuration);
        }

        // MARK: Logos
        // Logo designs live in their own file beside the configuration, so they are read and written
        // through their own endpoints rather than as part of the configuration payload.
        [HttpGet("Logos")]
        public IActionResult GetLogos([FromQuery] string? language = null)
        {
            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                _logger.LogError("Plugin instance was null in GET Logos.");
                return NotInitialized(language);
            }

            return Ok(plugin.PosterConfigService.GetLogoDesigns());
        }

        // MARK: POST
        // MARK: Logos
        [HttpPost("Logos")]
        public IActionResult SaveLogos([FromBody] LogoConfiguration[] logos, [FromQuery] string? language = null)
        {
            if (logos == null || logos.Length == 0)
            {
                return BadRequest(new { success = false, error = BaseTranslations.Get(TranslationKeys.ErrorLogoDesignRequired, language) });
            }

            try
            {
                var plugin = Plugin.Instance;
                if (plugin == null)
                {
                    _logger.LogError("Plugin instance was null in POST Logos.");
                    return NotInitialized(language);
                }

                plugin.SaveLogoDesigns(logos);
                return Ok(new { success = true });
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "Failed to save logo designs.");
                return BadRequest(new { success = false, error = BaseTranslations.Get(TranslationKeys.ErrorLogoDesignRequired, language) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save logo designs.");
                return BadRequest(new { success = false, error = BaseTranslations.Get(TranslationKeys.ErrorSaveLogoDesigns, language) });
            }
        }

        // MARK: PosterStyles
        // Returns each poster style, its description, and the shapes it can lay out, read from the
        // generators themselves so the UI never hardcodes them. The words are in the viewer's language.
        [HttpGet("PosterStyles")]
        public IActionResult GetPosterStyles([FromQuery] string? language = null)
        {
            return Ok(SettingOptions.Styles(language));
        }

        // MARK: SettingOptions
        // Returns the choices and the starting value for every setting, read from the settings
        // themselves, so the configuration page never keeps its own copy of an enum or a default.
        // The labels and help text are in the viewer's language.
        [HttpGet("SettingOptions")]
        public IActionResult GetSettingOptions([FromQuery] string? language = null)
        {
            return Ok(new
            {
                options = SettingOptions.All(language),
                defaults = SettingOptions.Defaults(),
                text = SettingOptions.Text(language),
                backdropText = SettingOptions.BackdropText(language),
                frameText = SettingOptions.FrameExtractionText(language),
                frameOptions = SettingOptions.FrameExtractionOptions(language),
                logoDefaults = SettingOptions.LogoDefaults()
            });
        }

        // MARK: Translations
        /// <summary>
        /// Returns the pages' strings in a language: the Jellyfin strings the plugin declares and its own. Anonymous,
        /// since the strings are not secret.
        /// </summary>
        /// <param name="language">The language, such as de or pt-BR, as the web client's display settings keep it.</param>
        /// <returns>The strings by key, or 304 when the page already has them.</returns>
        [HttpGet("Translations")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status304NotModified)]
        public IActionResult Translations([FromQuery] string? language) => BaseTranslations.Serve(language, Request, Response);

        // MARK: Fonts
        // Returns every font family a design can use, each once, uploaded first, then bundled,
        // then the ones installed on the server. The family names come from the font files.
        [HttpGet("Fonts")]
        public IActionResult GetFonts()
        {
            return Ok(FontUtils.GetFontFamilies().Select(entry => new
            {
                family = entry.Family,
                source = entry.Source.ToString()
            }));
        }

        // MARK: Fonts/Uploaded
        // Lists the fonts in the plugin's fonts folder, with the family and style each file gives.
        [HttpGet("Fonts/Uploaded")]
        public IActionResult GetUploadedFonts([FromQuery] string? language = null)
        {
            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                _logger.LogError("Plugin instance was null in GET Fonts/Uploaded.");
                return NotInitialized(language);
            }

            return Ok(plugin.FontStore.List().Select(DescribeUploadedFont));
        }

        // MARK: Fonts/Uploaded POST
        // Stores one .ttf or .otf font, sent as base64 so its name stays out of the URL. A refused
        // font returns one sentence in the viewer's language saying why.
        [HttpPost("Fonts/Uploaded")]
        [RequestSizeLimit(MaxFontRequestBytes)]
        public IActionResult UploadFont([FromBody] FontUploadRequest request, [FromQuery] string? language = null)
        {
            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                _logger.LogError("Plugin instance was null in POST Fonts/Uploaded.");
                return NotInitialized(language);
            }

            byte[]? data = null;
            if (!string.IsNullOrEmpty(request?.Data))
            {
                var buffer = new byte[request.Data.Length * 3 / 4];
                if (Convert.TryFromBase64String(request.Data, buffer, out var written))
                {
                    data = buffer.AsSpan(0, written).ToArray();
                }
            }

            try
            {
                var result = plugin.FontStore.Upload(request?.FileName, data);
                if (result.Face == null)
                {
                    return BadRequest(new { success = false, error = BaseTranslations.Get(result.Error ?? TranslationKeys.ErrorFontUnreadable, language) });
                }

                _logger.LogInformation("Font {FileName} uploaded.", FormatUtilities.SanitizeForLog(result.Face.FileName));
                return Ok(DescribeUploadedFont(result.Face));
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Failed to store an uploaded font.");
                return BadRequest(new { success = false, error = BaseTranslations.Get(TranslationKeys.ErrorFontNotSaved, language) });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Failed to store an uploaded font.");
                return BadRequest(new { success = false, error = BaseTranslations.Get(TranslationKeys.ErrorFontNotSaved, language) });
            }
        }

        // MARK: Fonts/Uploaded DELETE
        // Deletes one font from the plugin's fonts folder. The name travels in the body.
        [HttpDelete("Fonts/Uploaded")]
        public IActionResult DeleteFont([FromBody] FontDeleteRequest request, [FromQuery] string? language = null)
        {
            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                _logger.LogError("Plugin instance was null in DELETE Fonts/Uploaded.");
                return NotInitialized(language);
            }

            try
            {
                if (!plugin.FontStore.Delete(request?.FileName))
                {
                    return NotFound(new { success = false, error = BaseTranslations.Get(TranslationKeys.ErrorFontMissing, language) });
                }

                _logger.LogInformation("Font {FileName} deleted.", FormatUtilities.SanitizeForLog(request?.FileName));
                return Ok(new { success = true });
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Failed to delete a font.");
                return BadRequest(new { success = false, error = BaseTranslations.Get(TranslationKeys.ErrorFontNotDeleted, language) });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Failed to delete a font.");
                return BadRequest(new { success = false, error = BaseTranslations.Get(TranslationKeys.ErrorFontNotDeleted, language) });
            }
        }

        // NotInitialized
        // The answer for a request that arrives before the plugin has loaded, in the viewer's language.
        private ObjectResult NotInitialized(string? language)
            => StatusCode(StatusCodes.Status500InternalServerError, BaseTranslations.Get(TranslationKeys.ErrorPluginNotInitialized, language));

        // DescribeUploadedFont
        // Shapes one uploaded font for the Settings tab's list.
        private static object DescribeUploadedFont(FontFace face) => new
        {
            fileName = face.FileName,
            family = face.Family,
            style = face.Style
        };

        // MARK: POST
        [HttpPost("Configuration")]
        public IActionResult UpdateConfiguration([FromBody] PluginConfiguration newConfig, [FromQuery] string? language = null)
        {
            if (newConfig == null)
            {
                return BadRequest(new { success = false, error = BaseTranslations.Get(TranslationKeys.ErrorConfigurationRequired, language) });
            }

            try
            {
                var plugin = Plugin.Instance;
                if (plugin == null)
                {
                    _logger.LogError("Plugin instance was null in POST.");
                    return NotInitialized(language);
                }

                // Debug rather than Information: this fires on every save and expands the whole
                // configuration into the server log.
                _logger.LogDebug("Received config: {@NewConfig}", newConfig);

                // UpdateConfiguration rather than copying into the live configuration and saving: it
                // swaps the whole object in one step, so a reader never sees half the new values, and
                // it runs the plugin's override that rebuilds the design lookups and the frame pool.
                plugin.UpdateConfiguration(newConfig);

                _logger.LogInformation("Configuration saved successfully.");
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update configuration.");
                return BadRequest(new { success = false, error = BaseTranslations.Get(TranslationKeys.ErrorSaveConfiguration, language) });
            }
        }

        // MARK: Preview
        // Renders a design against the sample artwork at one shape. Every design renders both
        // shapes, so the Designs page asks for each. The item kind decides which text is shown.
        [HttpPost("Preview")]
        public IActionResult GeneratePreview(
            [FromBody] PosterSettings settings,
            [FromQuery] ArtworkItemKind kind = ArtworkItemKind.Series,
            [FromQuery] ArtworkShape shape = ArtworkShape.Landscape,
            [FromQuery] string? language = null)
        {
            if (settings == null)
            {
                return BadRequest(BaseTranslations.Get(TranslationKeys.ErrorPosterSettingsRequired, language));
            }

            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                _logger.LogError("Plugin instance was null in Preview.");
                return NotInitialized(language);
            }

            try
            {
                var imageBytes = plugin.PreviewService.GeneratePreview(settings, kind, shape);
                if (imageBytes == null)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, BaseTranslations.Get(TranslationKeys.ErrorPreview, language));
                }

                return File(imageBytes, "image/jpeg");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate poster preview.");
                return StatusCode(StatusCodes.Status500InternalServerError, BaseTranslations.Get(TranslationKeys.ErrorPreview, language));
            }
        }

        // MARK: Preview/Logo
        // Renders a logo design as a transparent PNG, for the sample series or a sample name.
        [HttpPost("Preview/Logo")]
        public IActionResult GenerateLogoPreview([FromBody] LogoSettings settings, [FromQuery] string? name = null, [FromQuery] string? language = null)
        {
            if (settings == null)
            {
                return BadRequest(BaseTranslations.Get(TranslationKeys.ErrorLogoSettingsRequired, language));
            }

            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                _logger.LogError("Plugin instance was null in logo Preview.");
                return NotInitialized(language);
            }

            try
            {
                var imageBytes = plugin.PreviewService.GenerateLogoPreview(settings, name?.Length > 200 ? name[..200] : name);
                if (imageBytes == null)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, BaseTranslations.Get(TranslationKeys.ErrorLogoPreview, language));
                }

                return File(imageBytes, "image/png");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate logo preview.");
                return StatusCode(StatusCodes.Status500InternalServerError, BaseTranslations.Get(TranslationKeys.ErrorLogoPreview, language));
            }
        }

        // MARK: PreviewComponent
        [HttpGet("Preview/Component/{component}")]
        public IActionResult GetPreviewComponent([FromRoute] string component, [FromQuery] string? language = null)
        {
            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                _logger.LogError("Plugin instance was null in PreviewComponent.");
                return NotInitialized(language);
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
        //
        // A token only ever names one image, so the response is marked immutable. The picker loads
        // each preview once ahead of time and again to paint it, and without this a browser can
        // fetch the full image a second time, leaving the card empty while it fades in.
        [HttpGet("Generated/{token}")]
        [AllowAnonymous]
        public IActionResult GetGeneratedImage([FromRoute] string token)
        {
            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                return NotInitialized(null);
            }

            if (!plugin.GeneratedImageCache.TryGet(token, out var imageBytes, out var contentType))
            {
                return NotFound();
            }

            Response.Headers.CacheControl = GeneratedImageCacheControl;
            return File(imageBytes, contentType);
        }
    }
}
