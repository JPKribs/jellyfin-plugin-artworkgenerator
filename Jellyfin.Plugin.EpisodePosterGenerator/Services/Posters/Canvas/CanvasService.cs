using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Services.Artwork;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Services
{
    /// <summary>
    /// Produces the bitmaps artwork is drawn on: extracted frames from the item's shared frame pool,
    /// the series backdrop, or a blank canvas, each cropped and brightened for the render.
    /// </summary>
    public class CanvasService
    {
        private const int DefaultHeight = 1080;
        private const int DefaultWidth = 1920;

        private readonly ILogger<CanvasService> _logger;
        private readonly FramePoolService _framePool;
        private readonly CroppingService _croppingService;
        private readonly BrightnessService _brightnessService;

        public CanvasService(
            ILogger<CanvasService> logger,
            FramePoolService framePool,
            CroppingService croppingService,
            BrightnessService brightnessService)
        {
            _logger = logger;
            _framePool = framePool;
            _croppingService = croppingService;
            _brightnessService = brightnessService;
        }

        /// <summary>
        /// Produces up to <paramref name="count"/> poster canvases, starting at rank
        /// <paramref name="rankOffset"/> of the item's frame pool. The settings must already be
        /// adjusted for <paramref name="shape"/>. Only extracted frames can produce more than one.
        /// When nothing can be extracted, the series backdrop and then a blank canvas stand in, so
        /// a series or season without local episodes still gets artwork. The caller owns the bitmaps.
        /// </summary>
        public async Task<IReadOnlyList<SKBitmap>> GenerateCanvasesAsync(
            BaseItem item,
            ArtworkSubject subject,
            PosterSettings settings,
            ArtworkShape shape,
            int rankOffset,
            int count,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            try
            {
                if (settings.CanvasSource == CanvasSource.Extract)
                {
                    var extracted = await ExtractCanvasesAsync(item, subject, settings, rankOffset, count, cancellationToken).ConfigureAwait(false);
                    if (extracted.Count > 0)
                    {
                        return extracted;
                    }

                    _logger.LogInformation("No frames could be extracted for {Name}; falling back to the series backdrop", item.Name);
                }

                if (settings.CanvasSource != CanvasSource.None)
                {
                    var backdrop = LoadSeriesBackdropCanvas(subject.VideoMetadata, settings);
                    if (backdrop != null)
                    {
                        return new[] { backdrop };
                    }
                }

                return new[] { CreateFallbackCanvas(subject.VideoMetadata, shape) };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating canvas for {Name}", item.Name);
                return Array.Empty<SKBitmap>();
            }
        }

        /// <summary>
        /// Produces up to <paramref name="count"/> backdrop bitmaps from the item's frame pool: the
        /// frame cropped to the backdrop ratio with no design applied. Returns nothing when the item
        /// has no extractable video.
        /// </summary>
        public async Task<IReadOnlyList<SKBitmap>> GenerateBackdropCanvasesAsync(
            BaseItem item,
            ArtworkSubject subject,
            BackdropSettings backdrop,
            int rankOffset,
            int count,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(backdrop);

            var settings = new PosterSettings
            {
                CanvasSource = CanvasSource.Extract,
                EnableLetterboxDetection = backdrop.EnableLetterboxDetection,
                LetterboxBlackThreshold = backdrop.LetterboxBlackThreshold,
                LetterboxConfidence = backdrop.LetterboxConfidence,
                BrightenHDR = backdrop.BrightenHDR,
                ExtractWindowStart = backdrop.ExtractWindowStart,
                ExtractWindowEnd = backdrop.ExtractWindowEnd,
                PosterFill = PosterFill.Fit,
                PosterDimensionRatio = string.IsNullOrWhiteSpace(backdrop.AspectRatio) ? "16:9" : backdrop.AspectRatio
            };

            try
            {
                return await ExtractCanvasesAsync(item, subject, settings, rankOffset, count, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating backdrop for {Name}", item.Name);
                return Array.Empty<SKBitmap>();
            }
        }

        // ExtractCanvasesAsync
        // Leases the item's frame pool and turns the requested ranks into prepared canvases.
        private async Task<IReadOnlyList<SKBitmap>> ExtractCanvasesAsync(
            BaseItem item,
            ArtworkSubject subject,
            PosterSettings settings,
            int rankOffset,
            int count,
            CancellationToken cancellationToken)
        {
            var sources = ArtworkSources.GetPlayableEpisodes(item);
            if (sources.Count == 0)
            {
                return Array.Empty<SKBitmap>();
            }

            var key = FramePoolService.KeyFor(item.Id, settings.ExtractWindowStart, settings.ExtractWindowEnd);

            using var lease = await _framePool.AcquireAsync(
                key,
                sources,
                settings.ExtractWindowStart,
                settings.ExtractWindowEnd,
                rankOffset + count,
                cancellationToken).ConfigureAwait(false);

            if (lease.Paths.Count == 0)
            {
                return Array.Empty<SKBitmap>();
            }

            var canvases = new List<SKBitmap>(count);
            var used = new HashSet<int>();

            try
            {
                for (int i = 0; i < count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // A pool smaller than the request wraps around rather than failing, but a
                    // single request never returns the same frame twice.
                    var index = (rankOffset + i) % lease.Paths.Count;
                    if (!used.Add(index))
                    {
                        break;
                    }

                    var canvas = PrepareFrame(lease.Paths[index], settings);
                    if (canvas != null)
                    {
                        canvases.Add(canvas);
                    }
                }
            }
            catch
            {
                foreach (var canvas in canvases)
                {
                    canvas.Dispose();
                }

                throw;
            }

            if (canvases.Count > 0)
            {
                subject.VideoMetadata.VideoWidth = canvases[0].Width;
                subject.VideoMetadata.VideoHeight = canvases[0].Height;
            }

            return canvases;
        }

        // PrepareFrame
        // Decodes a pooled frame, crops it, and brightens it. Pooled frames are kept raw so each
        // consumer can apply its own crop; the pool file itself is never modified.
        private SKBitmap? PrepareFrame(string path, PosterSettings settings)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var bitmap = SKBitmap.Decode(path);
            if (bitmap == null)
            {
                _logger.LogWarning("Failed to decode pooled frame {Path}", path);
                return null;
            }

            var canvas = _croppingService.CropPoster(bitmap, settings);

            // CropPoster hands back the source untouched when nothing needed cropping; the source
            // is disposed with the using above, so take a copy in that case.
            if (ReferenceEquals(canvas, bitmap))
            {
                canvas = bitmap.Copy();
            }

            if (settings.BrightenHDR > 0)
            {
                _brightnessService.BrightenBitmap(canvas, settings.BrightenHDR);
            }

            return canvas;
        }

        // LoadSeriesBackdropCanvas
        // Loads the series backdrop image and crops it to the render's settings. Returns null when no
        // backdrop is available or it cannot be decoded.
        private SKBitmap? LoadSeriesBackdropCanvas(VideoMetadata videoMeta, PosterSettings settings)
        {
            var backdropPath = videoMeta.SeriesBackdropFilePath;
            if (string.IsNullOrEmpty(backdropPath) || !File.Exists(backdropPath))
            {
                return null;
            }

            using var bitmap = SKBitmap.Decode(backdropPath);
            if (bitmap == null)
            {
                _logger.LogWarning("Failed to decode series backdrop: {BackdropPath}", backdropPath);
                return null;
            }

            var cropped = _croppingService.CropPoster(bitmap, settings);
            var canvas = ReferenceEquals(cropped, bitmap) ? bitmap.Copy() : cropped;

            videoMeta.VideoWidth = canvas.Width;
            videoMeta.VideoHeight = canvas.Height;

            return canvas;
        }

        // CreateFallbackCanvas
        // A transparent canvas in the render's shape: the video's size for landscape, and a 2:3
        // portrait of the same height for portrait.
        private SKBitmap CreateFallbackCanvas(VideoMetadata videoMeta, ArtworkShape shape)
        {
            var height = videoMeta.VideoHeight > 0 ? videoMeta.VideoHeight : DefaultHeight;
            var width = shape == ArtworkShape.Portrait
                ? height * 2 / 3
                : (videoMeta.VideoWidth > 0 ? videoMeta.VideoWidth : DefaultWidth);

            _logger.LogDebug("Creating fallback canvas {Width}x{Height}", width, height);
            var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            return bitmap;
        }
    }
}
