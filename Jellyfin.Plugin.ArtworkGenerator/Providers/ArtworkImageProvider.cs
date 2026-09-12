using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ArtworkGenerator.Services.Artwork;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtworkGenerator.Providers
{
    /// <summary>
    /// Generates artwork automatically during a metadata refresh, for every image the item's
    /// profile fills and the item is missing. Users who want to choose between alternates go
    /// through <see cref="ArtworkRemoteImageProvider"/> in the Edit Images dialog.
    /// </summary>
    /// <remarks>
    /// Jellyfin only runs this for an item type when the library lists the plugin as an image
    /// fetcher for that type, so series and season artwork stays off until a library enables it.
    /// </remarks>
    public class ArtworkImageProvider : IDynamicImageProvider
    {
        /// <summary>
        /// The provider name shown in library settings. Libraries store enabled fetchers by name, so
        /// changing it would silently switch the plugin off everywhere it was enabled.
        /// </summary>
        internal const string ProviderName = "Artwork Generator";

        private readonly ILogger<ArtworkImageProvider> _logger;
        private readonly IProviderManager _providerManager;

        public ArtworkImageProvider(ILogger<ArtworkImageProvider> logger, IProviderManager providerManager)
        {
            _logger = logger;
            _providerManager = providerManager;
        }

        public string Name => ProviderName;

        // Supports
        // Series, seasons, and episodes.
        public bool Supports(BaseItem item) => item != null && ArtworkService.GetKind(item) != null;

        // GetSupportedImages
        // The image types the item's profile has turned on.
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            var plugin = Plugin.Instance;
            if (item == null || plugin?.Configuration?.EnableProvider != true)
            {
                return Array.Empty<ImageType>();
            }

            return plugin.ArtworkService.GetEnabledImageTypes(item);
        }

        // GetImage
        // Generates one image of the requested type.
        public async Task<DynamicImageResponse> GetImage(BaseItem item, ImageType type, CancellationToken cancellationToken)
        {
            var plugin = Plugin.Instance;
            if (item == null || plugin?.Configuration?.EnableProvider != true)
            {
                return NoImage();
            }

            if (item is Episode episode && (string.IsNullOrEmpty(episode.Path) || !File.Exists(episode.Path)))
            {
                return NoImage();
            }

            try
            {
                var results = await plugin.ArtworkService.GenerateAsync(item, type, 1, cancellationToken).ConfigureAwait(false);
                if (results.Count == 0)
                {
                    _logger.LogWarning("No {Type} image could be generated for {Name}", type, item.Name);
                    return NoImage();
                }

                if (item is Episode && type == ImageType.Primary)
                {
                    await SaveEpisodeBackdropAsync(plugin, item, cancellationToken).ConfigureAwait(false);
                }

                var result = results[0];
                return new DynamicImageResponse
                {
                    HasImage = true,

                    // DynamicImageResponse takes ownership of the stream and disposes it.
                    Stream = new MemoryStream(result.Bytes),
                    Format = result.Format
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating {Type} image for {Name}", type, item.Name);
                return NoImage();
            }
        }

        private static DynamicImageResponse NoImage() => new() { HasImage = false };

        // SaveEpisodeBackdropAsync
        // Libraries commonly allow no episode backdrops, in which case a refresh never asks for
        // one. The episode's Backdrop slot is honoured here instead, alongside its poster and from
        // the same frame pool, which keeps what the earlier "save extracted frame as backdrop"
        // option did. It only fills a missing backdrop.
        private async Task SaveEpisodeBackdropAsync(Plugin plugin, BaseItem item, CancellationToken cancellationToken)
        {
            if (item.HasImage(ImageType.Backdrop))
            {
                return;
            }

            try
            {
                var results = await plugin.ArtworkService.GenerateAsync(item, ImageType.Backdrop, 1, cancellationToken).ConfigureAwait(false);
                if (results.Count == 0)
                {
                    return;
                }

                using (var stream = new MemoryStream(results[0].Bytes))
                {
                    await _providerManager.SaveImage(item, stream, results[0].MimeType, ImageType.Backdrop, null, cancellationToken).ConfigureAwait(false);
                }

                await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save backdrop for {Name}", item.Name);
            }
        }
    }
}
