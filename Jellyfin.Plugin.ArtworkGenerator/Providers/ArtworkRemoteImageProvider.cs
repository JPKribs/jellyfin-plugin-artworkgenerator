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
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.AspNetCore.Http;
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
        private readonly IConfigurationManager _configurationManager;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILibraryManager _libraryManager;

        public ArtworkRemoteImageProvider(
            ILogger<ArtworkRemoteImageProvider> logger,
            IServerApplicationHost appHost,
            IConfigurationManager configurationManager,
            IHttpContextAccessor httpContextAccessor,
            ILibraryManager libraryManager)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _appHost = appHost;
            _configurationManager = configurationManager;
            _httpContextAccessor = httpContextAccessor;
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

            // Jellyfin also queries remote providers during a metadata refresh: to fill a missing
            // image, to replace existing ones, or to top backdrops up to the library's limit. Only
            // the Edit Images dialog arrives as a request for the item's remote images; a refresh
            // runs with no request at all, or inside a request for something else.
            var picking = IsImagePickerRequest(_httpContextAccessor?.HttpContext?.Request.Path.Value);
            var backdropLimit = picking ? 0 : GetBackdropLimit(item);

            var baseUrl = GetLocalBaseUrl();
            if (baseUrl == null)
            {
                _logger.LogError("Could not determine this server's local API URL; generated artwork cannot be offered in the image picker");
                return Array.Empty<RemoteImageInfo>();
            }

            // The dialog asks for everything again on every type switch and page, so a set rendered
            // moments ago is offered again while nothing it was drawn from has changed.
            var stamp = GeneratedCandidateMemo.StampFor(item, plugin.PosterConfigService.Version);
            if (picking && plugin.CandidateMemo.TryGet(item.Id, stamp, plugin.GeneratedImageCache, out var remembered))
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

            foreach (var (type, count) in Plan(plugin.ArtworkService.GetEnabledImageTypes(item), picking, plugin.Configuration.ImageChoiceCount, backdropLimit))
            {
                var choosing = picking;

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
            // A refresh's set holds one image of each type, so it is never remembered for the
            // picker to offer.
            if (complete && picking)
            {
                plugin.CandidateMemo.Set(item.Id, stamp, images, tokens);
            }

            _logger.LogDebug("Offering {Count} generated image(s) for {Name}", images.Count, item.Name);
            return images;
        }

        // Plan
        // How many images of each enabled type to render. The picker offers the configured number
        // of choices in every design the slot lists. A refresh renders every type, since it may be
        // replacing images the item already has, but keeps one of each, drawn with the slot's first
        // design, so only one is drawn; backdrops are filled up to the library's limit from the
        // same list, so that many are drawn. A color logo is text only, so the artwork service
        // returns just one whatever is asked.
        internal static IReadOnlyList<(ImageType Type, int Count)> Plan(IReadOnlyList<ImageType> enabled, bool picking, int choiceCount, int backdropLimit)
        {
            var plan = new List<(ImageType Type, int Count)>(enabled.Count);
            foreach (var type in enabled)
            {
                var count = picking
                    ? Math.Clamp(choiceCount, 1, ArtworkService.MaxCandidates)
                    : type == ImageType.Backdrop ? Math.Clamp(backdropLimit, 1, ArtworkService.MaxCandidates) : 1;
                plan.Add((type, count));
            }

            return plan;
        }

        // GetBackdropLimit
        // The number of backdrops the item's library keeps for its type, read the same way
        // Jellyfin's own refresh reads it. One when it cannot be read.
        private int GetBackdropLimit(BaseItem item)
        {
            try
            {
                var typeName = item.GetType().Name;
                var options = _libraryManager?.GetLibraryOptions(item)?.GetTypeOptions(typeName) ?? new TypeOptions { Type = typeName };
                return options.GetLimit(ImageType.Backdrop);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read the backdrop limit for {Name}", item.Name);
                return 1;
            }
        }

        // IsImagePickerRequest
        // True for the request the Edit Images dialog makes for an item's remote images,
        // Items/{id}/RemoteImages, behind any base URL the server is configured with.
        internal static bool IsImagePickerRequest(string? path)
        {
            return !string.IsNullOrEmpty(path)
                && path.TrimEnd('/').EndsWith("/RemoteImages", StringComparison.OrdinalIgnoreCase);
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

            return FallbackBaseUrl(_appHost.HttpPort, GetConfiguredBaseUrl());
        }

        // GetConfiguredBaseUrl
        // The URL prefix the server is configured to answer under, such as "/jellyfin".
        private string? GetConfiguredBaseUrl()
        {
            try
            {
                return _configurationManager?.GetNetworkConfiguration()?.BaseUrl;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read the server's base URL");
                return null;
            }
        }

        // FallbackBaseUrl
        // Loopback on the HTTP port, under the server's base URL, since the server only answers
        // there when one is configured.
        internal static string? FallbackBaseUrl(int port, string? configuredBaseUrl)
        {
            if (port <= 0)
            {
                return null;
            }

            var prefix = string.IsNullOrWhiteSpace(configuredBaseUrl) ? string.Empty : "/" + configuredBaseUrl.Trim().Trim('/');
            return string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}{prefix}").TrimEnd('/');
        }
    }
}
