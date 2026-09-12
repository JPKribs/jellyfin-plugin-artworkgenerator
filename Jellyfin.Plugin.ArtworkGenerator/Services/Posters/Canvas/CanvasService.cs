using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using Jellyfin.Plugin.ArtworkGenerator.Services.Posters;
using Jellyfin.Plugin.ArtworkGenerator.Services.Artwork;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.ArtworkGenerator.Services
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
                if (settings.CanvasSource == CanvasSource.Grid)
                {
                    var grids = await GridCanvasesAsync(item, subject, settings, rankOffset, count, cancellationToken).ConfigureAwait(false);
                    if (grids.Count > 0)
                    {
                        return grids;
                    }

                    _logger.LogInformation("No frames could be extracted for {Name}; falling back to the series backdrop", item.Name);
                }

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
        /// Takes a hold on the item's frame pool without growing it, so the pool is not discarded
        /// while several renders draw from it in turn. Returns null when the item has no video to
        /// extract from. Dispose the hold once the renders are done.
        /// </summary>
        public async Task<IDisposable?> HoldFramePoolAsync(BaseItem item, PosterSettings settings, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(settings);

            var sources = ArtworkSources.GetPlayableSources(item);
            if (sources.Count == 0)
            {
                return null;
            }

            var key = FramePoolService.KeyFor(item.Id, settings.ExtractWindowStart, settings.ExtractWindowEnd);
            return await _framePool.AcquireAsync(key, sources, settings.ExtractWindowStart, settings.ExtractWindowEnd, 0, cancellationToken).ConfigureAwait(false);
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

            // A backdrop is a frame with no design on it, so the only thing it needs beyond the
            // server's extraction settings is the shape it is cropped to.
            var settings = BasePosterGenerator.WithFrameExtraction(
                new PosterSettings
                {
                    CanvasSource = CanvasSource.Extract,
                    PosterFill = PosterFill.Fit,
                    PosterDimensionRatio = string.IsNullOrWhiteSpace(backdrop.AspectRatio) ? "16:9" : backdrop.AspectRatio
                },
                Plugin.Instance?.Configuration?.FrameExtraction);

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
            var sources = ArtworkSources.GetPlayableSources(item);
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
        // GridCanvasesAsync
        // One canvas per requested candidate, each a grid of frames rather than a single one. The
        // pool is asked for enough frames to fill every grid, so no two candidates are the same.
        private async Task<IReadOnlyList<SKBitmap>> GridCanvasesAsync(
            BaseItem item,
            ArtworkSubject subject,
            PosterSettings settings,
            int rankOffset,
            int count,
            CancellationToken cancellationToken)
        {
            var sources = ArtworkSources.GetPlayableSources(item);
            if (sources.Count == 0)
            {
                return Array.Empty<SKBitmap>();
            }

            var cells = GridComposer.Clamp(settings.GridFrames);
            var key = FramePoolService.KeyFor(item.Id, settings.ExtractWindowStart, settings.ExtractWindowEnd);

            using var lease = await _framePool.AcquireAsync(
                key,
                sources,
                settings.ExtractWindowStart,
                settings.ExtractWindowEnd,
                (rankOffset + count) * cells,
                cancellationToken).ConfigureAwait(false);

            if (lease.Paths.Count == 0)
            {
                return Array.Empty<SKBitmap>();
            }

            var canvases = new List<SKBitmap>(count);

            try
            {
                for (var candidate = 0; candidate < count; candidate++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var grid = ComposeGrid(lease.Paths, (rankOffset + candidate) * cells, cells, settings);
                    if (grid != null)
                    {
                        canvases.Add(grid);
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

            return canvases;
        }

        // ComposeGrid
        // The first frame is prepared the usual way to settle the canvas size, and every frame is
        // then tiled into it. A pool smaller than the grid wraps around rather than leaving holes.
        private SKBitmap? ComposeGrid(IReadOnlyList<string> paths, int offset, int cells, PosterSettings settings)
        {
            using var shaped = PrepareFrame(paths[offset % paths.Count], settings);
            if (shaped == null)
            {
                return null;
            }

            var frames = new List<SKBitmap>(cells);

            try
            {
                for (var i = 0; i < cells; i++)
                {
                    var frame = SKBitmap.Decode(paths[(offset + i) % paths.Count]);
                    if (frame != null)
                    {
                        frames.Add(frame);
                    }
                }

                var gap = Math.Min(shaped.Width, shaped.Height) * (Math.Clamp(settings.GridGap, 0f, 25f) / 100f);
                return GridComposer.Compose(frames, shaped.Width, shaped.Height, gap);
            }
            finally
            {
                foreach (var frame in frames)
                {
                    frame.Dispose();
                }
            }
        }

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
