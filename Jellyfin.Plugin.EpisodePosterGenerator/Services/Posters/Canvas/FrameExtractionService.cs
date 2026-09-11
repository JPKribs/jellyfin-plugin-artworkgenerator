using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Utilities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Services
{
    /// <summary>
    /// Extracts high-quality video frames using Jellyfin's media encoder,
    /// scoring candidates by brightness and sharpness to select the best frames.
    /// </summary>
    public class FrameExtractionService
    {
        private const int MaxRetries = 30;
        private const int ExtraAttemptsPerCandidate = 6;
        private const int MaxAttemptCeiling = 48;
        private const int EarlyExitAttemptThreshold = 5;
        private const double BrightnessThreshold = 0.05;
        private const double SharpnessThreshold = 100.0;
        private const double EarlyExitScoreThreshold = 0.6;
        private const double DefaultDurationSeconds = 3600;
        private const double DefaultSeekStartPercent = 0.2;
        private const double DefaultSeekEndPercent = 0.8;
        private const double BrightnessWeight = 0.5;
        private const int AnalysisSize = 200;

        private readonly ILogger<FrameExtractionService> _logger;
        private readonly IMediaEncoder _mediaEncoder;

        public FrameExtractionService(
            ILogger<FrameExtractionService> logger,
            IMediaEncoder mediaEncoder)
        {
            _logger = logger;
            _mediaEncoder = mediaEncoder;
        }

        /// <summary>
        /// Extracts up to <paramref name="count"/> distinct frames from an episode, best first.
        /// Successive attempts walk a low-discrepancy sequence across the extraction window, so
        /// candidates are spread through the episode rather than clustered. The walk starts at
        /// <paramref name="seekPhase"/> and skips its first <paramref name="attemptOffset"/> steps,
        /// so the same phase and offset always visit the same timestamps. The caller owns the
        /// returned files and is responsible for deleting them.
        /// </summary>
        public async Task<IReadOnlyList<ExtractedFrame>> ExtractFrameCandidatesAsync(
            Episode episode,
            float windowStartPercent,
            float windowEndPercent,
            int count,
            double seekPhase,
            int attemptOffset,
            CancellationToken cancellationToken = default)
        {
            if (episode == null || string.IsNullOrEmpty(episode.Path))
            {
                _logger.LogError("Invalid episode provided to FrameExtractionService");
                return Array.Empty<ExtractedFrame>();
            }

            count = Math.Max(1, count);

            var mediaSources = episode.GetMediaSources(false);
            var mediaSource = mediaSources?.Count > 0 ? mediaSources[0] : null;
            if (mediaSource == null)
            {
                _logger.LogError("No media source found for episode: {Path}", episode.Path);
                return Array.Empty<ExtractedFrame>();
            }

            var videoStream = mediaSource.MediaStreams?
                .FirstOrDefault(s => s.Type == MediaStreamType.Video);
            if (videoStream == null)
            {
                _logger.LogError("No video stream found for episode: {Path}", episode.Path);
                return Array.Empty<ExtractedFrame>();
            }

            var container = Path.GetExtension(episode.Path)?.TrimStart('.') ?? string.Empty;
            var videoDurationSeconds = (episode.RunTimeTicks ?? 0) / (double)TimeSpan.TicksPerSecond;
            if (videoDurationSeconds <= 0) videoDurationSeconds = DefaultDurationSeconds;

            _logger.LogInformation("Extracting {Count} frame(s) from {Path} (duration: {Duration}s, container: {Container})",
                count, episode.Path, (int)videoDurationSeconds, container);

            // Candidates kept so far, worst-scoring first so eviction is cheap.
            var candidates = new List<FrameCandidate>(count);
            var goodCount = 0;

            var maxAttempts = Math.Clamp(
                MaxRetries + ((count - 1) * ExtraAttemptsPerCandidate),
                MaxRetries,
                MaxAttemptCeiling);

            try
            {
                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string? extractedPath = null;
                    bool keepFile = false;

                    try
                    {
                        var seekSeconds = GenerateSeekTime(videoDurationSeconds, attemptOffset + attempt, seekPhase, windowStartPercent, windowEndPercent);
                        var offset = TimeSpan.FromSeconds(seekSeconds);

                        extractedPath = await _mediaEncoder.ExtractVideoImage(
                            episode.Path,
                            container,
                            mediaSource,
                            videoStream,
                            null,
                            offset,
                            cancellationToken).ConfigureAwait(false);

                        if (string.IsNullOrEmpty(extractedPath) || !File.Exists(extractedPath))
                        {
                            if (attempt == 0)
                            {
                                _logger.LogWarning("ExtractVideoImage returned no output on first attempt");
                            }
                            continue;
                        }

                        double brightness;
                        double sharpness;
                        using (var stream = File.OpenRead(extractedPath))
                        using (var frameBitmap = SKBitmap.Decode(stream))
                        {
                            if (frameBitmap == null)
                            {
                                continue;
                            }

                            using var analysisBitmap = CreateAnalysisBitmap(frameBitmap);
                            AnalyzeFrame(analysisBitmap, out brightness, out sharpness);
                        }

                        var qualityScore = CalculateQualityScore(brightness, sharpness);
                        var isGood = brightness > BrightnessThreshold && sharpness >= SharpnessThreshold;

                        if (attempt < EarlyExitAttemptThreshold || isGood)
                        {
                            _logger.LogDebug("Attempt {Attempt}: Brightness {Brightness:F3}, Sharpness {Sharpness:F1}, Score {Score:F3}",
                                attempt + 1, brightness, sharpness, qualityScore);
                        }

                        keepFile = TryAddCandidate(candidates, count, extractedPath, qualityScore, isGood, ref goodCount);

                        // Enough frames that clear both thresholds outright: stop early.
                        if (goodCount >= count)
                        {
                            _logger.LogInformation("Found {Count} high-quality frame(s) after {Attempts} attempt(s)",
                                goodCount, attempt + 1);
                            break;
                        }

                        // Otherwise settle for a full set of merely acceptable frames once the
                        // cheap attempts are spent, rather than exhausting every retry.
                        if (candidates.Count >= count
                            && attempt > EarlyExitAttemptThreshold
                            && candidates.All(c => c.Score > EarlyExitScoreThreshold))
                        {
                            _logger.LogInformation("Found {Count} acceptable frame(s) after {Attempts} attempt(s)",
                                candidates.Count, attempt + 1);
                            break;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Frame extraction failed on attempt {Attempt}", attempt + 1);
                        if (attempt < 3)
                        {
                            continue;
                        }
                        break;
                    }
                    finally
                    {
                        if (!keepFile)
                        {
                            TryDeleteFile(extractedPath);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation mid-run must not strand extracted frames on disk.
                foreach (var candidate in candidates)
                {
                    TryDeleteFile(candidate.Path);
                }

                throw;
            }

            if (candidates.Count == 0)
            {
                _logger.LogError("Failed to extract any usable frames after {Attempts} attempts", maxAttempts);
                return Array.Empty<ExtractedFrame>();
            }

            var ordered = candidates
                .OrderByDescending(c => c.Score)
                .Select(c => new ExtractedFrame(c.Path, c.Score))
                .ToArray();

            _logger.LogInformation("Using {Count} frame(s) (best score: {Score:F3})",
                ordered.Length, candidates.Max(c => c.Score));

            return ordered;
        }

        // TryAddCandidate
        // Keeps the frame if there is room or it beats the weakest candidate held so far.
        // Returns true when the file was retained (and so must not be deleted by the caller).
        private static bool TryAddCandidate(
            List<FrameCandidate> candidates,
            int capacity,
            string path,
            double score,
            bool isGood,
            ref int goodCount)
        {
            if (candidates.Count < capacity)
            {
                candidates.Add(new FrameCandidate(path, score, isGood));
                if (isGood) goodCount++;
                return true;
            }

            var weakestIndex = 0;
            for (int i = 1; i < candidates.Count; i++)
            {
                if (candidates[i].Score < candidates[weakestIndex].Score)
                {
                    weakestIndex = i;
                }
            }

            if (score <= candidates[weakestIndex].Score)
            {
                return false;
            }

            var evicted = candidates[weakestIndex];
            TryDeleteFile(evicted.Path);
            if (evicted.IsGood) goodCount--;

            candidates[weakestIndex] = new FrameCandidate(path, score, isGood);
            if (isGood) goodCount++;
            return true;
        }

        private int GenerateSeekTime(double videoDurationSeconds, int attempt, double seekPhase, float windowStart, float windowEnd)
        {
            var startPercent = windowStart / 100.0;
            var endPercent = windowEnd / 100.0;

            if (startPercent >= endPercent)
            {
                _logger.LogWarning("Invalid extraction window: start {Start}% >= end {End}%, using default 20%-80%",
                    windowStart, windowEnd);
                startPercent = DefaultSeekStartPercent;
                endPercent = DefaultSeekEndPercent;
            }

            var startTime = videoDurationSeconds * startPercent;
            var endTime = videoDurationSeconds * endPercent;

            // Low-discrepancy (golden ratio) sequence: successive attempts land far apart and
            // never resample the same region, unlike random seeks which can cluster or repeat.
            // The phase comes from the frame pool's seed, so a new pool probes new frames while the
            // same seed revisits the same ones.
            var fraction = (seekPhase + attempt * 0.6180339887498949) % 1.0;
            return (int)(startTime + fraction * (endTime - startTime));
        }

        private static SKBitmap? CreateAnalysisBitmap(SKBitmap source)
        {
            if (source == null) return null;

            float scale = Math.Min((float)AnalysisSize / source.Width, (float)AnalysisSize / source.Height);
            int newWidth = Math.Max(1, (int)(source.Width * scale));
            int newHeight = Math.Max(1, (int)(source.Height * scale));

            var resized = new SKBitmap(newWidth, newHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(resized);
            PaintFactory.DrawBitmap(canvas, source, SKRect.Create(source.Width, source.Height), SKRect.Create(newWidth, newHeight), null, RenderConstants.FastSampling);

            return resized;
        }

        // AnalyzeFrame
        // Computes mean luminance and Laplacian variance from a single pixel snapshot, so the
        // analysis bitmap is only marshalled to managed memory once per frame.
        private static void AnalyzeFrame(SKBitmap? analysis, out double brightness, out double sharpness)
        {
            brightness = 0.0;
            sharpness = 0.0;

            if (analysis == null) return;

            var pixels = analysis.Pixels;
            if (pixels == null || pixels.Length == 0) return;

            int width = analysis.Width;
            int height = analysis.Height;

            var luma = new double[pixels.Length];
            double totalLuma = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];
                var value = (0.2126 * c.Red) + (0.7152 * c.Green) + (0.0722 * c.Blue);
                luma[i] = value;
                totalLuma += value;
            }

            brightness = totalLuma / pixels.Length / 255.0;

            double sum = 0;
            int count = 0;
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    int c = (y * width) + x;
                    double lap = (4 * luma[c])
                        - luma[((y - 1) * width) + x]
                        - luma[((y + 1) * width) + x]
                        - luma[(y * width) + (x - 1)]
                        - luma[(y * width) + (x + 1)];
                    sum += lap * lap;
                    count++;
                }
            }

            sharpness = count > 0 ? sum / count : 0.0;
        }

        private static double CalculateQualityScore(double brightness, double sharpness)
        {
            double normalizedBrightness = Math.Min(brightness / BrightnessThreshold, 1.0);
            double normalizedSharpness = Math.Min(sharpness / SharpnessThreshold, 1.0);
            double sharpnessWeight = 1.0 - BrightnessWeight;

            return (normalizedBrightness * BrightnessWeight) + (normalizedSharpness * sharpnessWeight);
        }

        private static void TryDeleteFile(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private readonly record struct FrameCandidate(string Path, double Score, bool IsGood);
    }

    /// <summary>
    /// An extracted frame file and its quality score.
    /// </summary>
    public readonly record struct ExtractedFrame(string Path, double Score);
}
