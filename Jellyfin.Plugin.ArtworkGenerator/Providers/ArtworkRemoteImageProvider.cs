using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ArtworkGenerator.Services.Artwork;
using Jellyfin.Plugin.ArtworkGenerator.Services.Posters;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtworkGenerator.Providers
{
    /// <summary>
    /// Offers freshly generated artwork in an item's Edit Images dialog, so an image can be picked
    /// by hand instead of only arriving through a metadata refresh.
    /// </summary>
    /// <remarks>
    /// Jellyfin renders the picker by fetching each <see cref="RemoteImageInfo.Url"/> with the
    /// server's own HTTP client, and downloads the chosen one the same way. Those requests carry
    /// no user credentials, so the images are rendered here, inside an authenticated call, and
    /// parked in <see cref="Services.Posters.GeneratedImageCache"/> under an unguessable token.
    /// The URLs point back at this server over loopback and resolve to a plain cache lookup.
    /// </remarks>
    public class ArtworkRemoteImageProvider : IRemoteImageProvider, IHasOrder
    {
        /// <summary>
        /// Route serving cached candidates. Must stay in step with ConfigurationController.
        /// </summary>
        internal const string GeneratedImageRoute = "Plugins/ArtworkGenerator/Generated";

        private readonly ILogger<ArtworkRemoteImageProvider> _logger;
        private readonly IServerApplicationHost _appHost;

        public ArtworkRemoteImageProvider(ILogger<ArtworkRemoteImageProvider> logger, IServerApplicationHost appHost)
        {
            _logger = logger;
            _appHost = appHost;
        }

        public string Name => ArtworkImageProvider.ProviderName;

        /// <summary>
        /// Gets the ordering hint. Listed after the metadata providers so real artwork still takes
        /// precedence when Jellyfin picks a default.
        /// </summary>
        public int Order => 100;

        // Supports
        // Series, seasons, episodes, films, and any other standalone video.
        public bool Supports(BaseItem item) => item != null && ArtworkService.GetKind(item) != null;

        // GetSupportedImages
        // The image types the item's profile has turned on.
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            var plugin = Plugin.Instance;
            if (item == null || plugin?.Configuration == null)
            {
                return Array.Empty<ImageType>();
            }

            return plugin.ArtworkService.GetEnabledImageTypes(item);
        }

        // GetImages
        // Renders candidates for every enabled image type and publishes them as remote images.
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var plugin = Plugin.Instance;
            if (item == null || plugin?.Configuration == null)
            {
                return Array.Empty<RemoteImageInfo>();
            }

            if (item is Episode episode && (string.IsNullOrEmpty(episode.Path) || !File.Exists(episode.Path)))
            {
                _logger.LogDebug("Episode {EpisodeName} has no readable media file; no artwork to offer", episode.Name);
                return Array.Empty<RemoteImageInfo>();
            }

            var baseUrl = GetLocalBaseUrl();
            if (baseUrl == null)
            {
                _logger.LogError("Could not determine this server's local API URL; generated artwork cannot be offered in the image picker");
                return Array.Empty<RemoteImageInfo>();
            }

            // The dialog asks for everything again on every type switch and page, so a set rendered
            // moments ago is offered again while nothing it was drawn from has changed.
            var stamp = GeneratedCandidateMemo.StampFor(item, plugin.PosterConfigService.Version);
            if (plugin.CandidateMemo.TryGet(item.Id, stamp, plugin.GeneratedImageCache, out var remembered))
            {
                _logger.LogDebug("Offering {Count} remembered image(s) for {Name}", remembered.Count, item.Name);
                return remembered;
            }

            var images = new List<RemoteImageInfo>();
            var tokens = new List<string>();
            var complete = true;

            // The picker groups and filters by language. These are drawn from the item's own frames
            // and its own name, so they belong to whatever language the item's metadata is in: the
            // library's setting where it has one, the server's otherwise.
            var language = item.PreferredMetadataLanguage;

            foreach (var type in plugin.ArtworkService.GetEnabledImageTypes(item))
            {
                // Jellyfin also queries remote providers during a metadata refresh, for items
                // missing an image type, and nothing distinguishes that from a user opening the
                // picker. The count is therefore bounded by what the item already has: with no
                // image of this type the caller is filling a blank and keeps exactly one, drawn
                // with the slot's first design. A color logo is text only, so the artwork service
                // returns just one whatever is asked.
                var choosing = item.HasImage(type);
                var count = !choosing
                    ? 1
                    : Math.Clamp(plugin.Configuration.ImageChoiceCount, 1, ArtworkService.MaxCandidates);

                try
                {
                    var generated = await plugin.ArtworkService.GenerateAsync(item, type, count, choosing, cancellationToken).ConfigureAwait(false);
                    foreach (var artwork in generated)
                    {
                        var token = plugin.GeneratedImageCache.Add(artwork.Bytes, artwork.MimeType);
                        var url = string.Create(CultureInfo.InvariantCulture, $"{baseUrl}/{GeneratedImageRoute}/{token}");

                        tokens.Add(token);
                        images.Add(new RemoteImageInfo
                        {
                            ProviderName = Name,
                            Url = url,
                            ThumbnailUrl = url,
                            Type = type,
                            Width = artwork.Width,
                            Height = artwork.Height,
                            Language = language
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    complete = false;
                    _logger.LogError(ex, "Failed to generate {Type} candidates for {Name}", type, item.Name);
                }
            }

            // A set missing a type that failed is not remembered, so the next request tries it again.
            if (complete)
            {
                plugin.CandidateMemo.Set(item.Id, stamp, images, tokens);
            }

            _logger.LogInformation("Offering {Count} generated image(s) for {Name}", images.Count, item.Name);
            return images;
        }

        // GetImageResponse
        // Serves a cached candidate directly. Jellyfin calls this on the metadata refresh path
        // instead of going over the network, so the bytes are returned without an HTTP round trip.
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var plugin = Plugin.Instance;
            var token = ExtractToken(url);

            if (plugin != null && token != null && plugin.GeneratedImageCache.TryGet(token, out var bytes, out var contentType))
            {
                var content = new ByteArrayContent(bytes);
                content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }

            _logger.LogWarning("Generated image for the requested URL is no longer available");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        // ExtractToken
        // Pulls the cache token off the tail of a generated image URL.
        private static string? ExtractToken(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }

            var trimmed = url.TrimEnd('/');
            var lastSlash = trimmed.LastIndexOf('/');
            if (lastSlash < 0 || lastSlash == trimmed.Length - 1)
            {
                return null;
            }

            return trimmed[(lastSlash + 1)..];
        }

        // GetLocalBaseUrl
        // Resolves the URL the server can use to reach itself. Loopback over plain HTTP avoids
        // depending on a publicly resolvable hostname or a certificate the server would have to
        // trust when it fetches its own image.
        private string? GetLocalBaseUrl()
        {
            try
            {
                var url = _appHost.GetApiUrlForLocalAccess(IPAddress.Loopback, allowHttps: false);
                if (!string.IsNullOrEmpty(url))
                {
                    return url.TrimEnd('/');
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve the local API URL; falling back to the configured HTTP port");
            }

            var port = _appHost.HttpPort;
            return port > 0
                ? string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}")
                : null;
        }
    }
}
