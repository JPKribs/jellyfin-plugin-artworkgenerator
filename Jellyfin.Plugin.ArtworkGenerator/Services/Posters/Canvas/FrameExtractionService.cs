using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.ArtworkGenerator.Services
{
    /// <summary>
    /// Extracts high-quality video frames using Jellyfin's media encoder,
    /// scoring candidates by brightness and sharpness to select the best frames.
    /// </summary>
    public class FrameExtractionService
    {
        // How many frames are looked at before the best are kept. The scorer ranks rather than
        // gates, so it needs a field to choose from: sampling one frame and taking it, which is what
        // an early exit amounted to on real video, is the same as not scoring at all.
        private const int MinimumSamples = 8;
        private const int SamplesPerCandidate = 4;
        private const int MaxAttemptCeiling = 48;
        private const int ConsecutiveFailureLimit = 4;

        // A frame this dark once its bars are off is a fade or a blackout, not a picture.
        private const double MinimumBrightness = 0.02;

        // Reference points the metrics are measured against: the value at which a measure counts as
        // fully satisfied. Nothing is clamped below them, so frames rank against each other across
        // the ordinary range rather than piling up at the top, which is what the old pass marks did.
        // Calibrated against sixty frames sampled across four episodes, measured at the analysis
        // size below and with the bars already removed, so they sit near the top of what real video
        // actually produces rather than at a theoretical maximum.
        private const double IdealBrightness = 0.42;
        private const double BrightnessSpread = 0.18;
        private const double SharpnessReference = 400.0;
        private const double ContrastReference = 0.22;
        private const double ColorfulnessReference = 0.08;

        private const double ToneWeight = 0.30;
        private const double DetailWeight = 0.30;
        private const double ContrastWeight = 0.12;
        private const double ColorfulnessWeight = 0.13;
        private const double HeadroomWeight = 0.15;

        private const double DefaultDurationSeconds = 3600;
        private const double DefaultSeekStartPercent = 0.2;
        private const double DefaultSeekEndPercent = 0.8;

        // Laplacian variance measures fine detail, which a small analysis bitmap throws away before
        // it can be counted. 480 keeps enough of it to tell focus from a flat wall.
        private const int AnalysisSize = 480;

        // The share of the frame taken as the top and bottom bands when judging how much room a
        // design has for its text.
        private const double TextBandRatio = 0.28;

        // A row or column is part of a letterbox bar when every pixel sampled along it is this
        // dark. Analysis only: the rendered image is cropped separately by the cropping service.
        private const int BarLuma = 18;

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
            Video episode,
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

            // Every sample is taken before any is chosen. The old loop stopped at the first frame
            // over a pass mark, which on real video was almost always the very first one.
            var maxAttempts = Math.Clamp(count * SamplesPerCandidate, MinimumSamples, MaxAttemptCeiling);
            var consecutiveFailures = 0;

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

                        FrameQuality quality;
                        using (var stream = File.OpenRead(extractedPath))
                        using (var frameBitmap = SKBitmap.Decode(stream))
                        {
                            if (frameBitmap == null)
                            {
                                continue;
                            }

                            using var analysisBitmap = CreateAnalysisBitmap(frameBitmap);
                            quality = AnalyzeFrame(analysisBitmap);
                        }

                        consecutiveFailures = 0;

                        // A fade or a blackout is not a picture, whatever else it measures well on.
                        var qualityScore = quality.Brightness > MinimumBrightness
                            ? CalculateQualityScore(quality)
                            : 0.0;

                        _logger.LogDebug(
                            "Attempt {Attempt}: brightness {Brightness:F3}, contrast {Contrast:F3}, sharpness {Sharpness:F0}, color {Color:F3}, headroom {Headroom:F2}, score {Score:F3}",
                            attempt + 1, quality.Brightness, quality.Contrast, quality.Sharpness, quality.Colorfulness, quality.Headroom, qualityScore);

                        keepFile = TryAddCandidate(candidates, count, extractedPath, qualityScore);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Frame extraction failed on attempt {Attempt}", attempt + 1);

                        // A run of failures means the file cannot be read, not that this timestamp
                        // was unlucky, so the sweep stops rather than working through every seek.
                        if (++consecutiveFailures >= ConsecutiveFailureLimit)
                        {
                            break;
                        }

                        continue;
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

            _logger.LogInformation("Using {Count} of {Sampled} sampled frame(s) (best score: {Score:F3}, worst kept: {Worst:F3})",
                ordered.Length, maxAttempts, ordered[0].Score, ordered[^1].Score);

            return ordered;
        }

        // TryAddCandidate
        // Keeps the frame if there is room or it beats the weakest candidate held so far.
        // Returns true when the file was retained (and so must not be deleted by the caller).
        private static bool TryAddCandidate(
            List<FrameCandidate> candidates,
            int capacity,
            string path,
            double score)
        {
            if (candidates.Count < capacity)
            {
                candidates.Add(new FrameCandidate(path, score));
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

            TryDeleteFile(candidates[weakestIndex].Path);
            candidates[weakestIndex] = new FrameCandidate(path, score);
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

        // CreateAnalysisBitmap
        // A downscaled copy with any letterbox bars removed. The bars are not picture, and scoring
        // them as if they were dragged every measurement toward black: on a 2.39:1 film in a 16:9
        // container they are a quarter of the frame and understated its brightness by a third.
        internal static SKBitmap? CreateAnalysisBitmap(SKBitmap source)
        {
            if (source == null) return null;

            var content = FindContentBounds(source);
            if (content.Width <= 0 || content.Height <= 0)
            {
                return null;
            }

            float scale = Math.Min((float)AnalysisSize / content.Width, (float)AnalysisSize / content.Height);
            int newWidth = Math.Max(1, (int)(content.Width * scale));
            int newHeight = Math.Max(1, (int)(content.Height * scale));

            var resized = new SKBitmap(newWidth, newHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(resized);
            PaintFactory.DrawBitmap(canvas, source, content, SKRect.Create(newWidth, newHeight), null, RenderConstants.FastSampling);

            return resized;
        }

        // FindContentBounds
        // The part of the frame that is picture rather than letterbox or pillarbox bar. Rows and
        // columns are sampled rather than read whole, which is enough to spot a solid black band.
        private static SKRect FindContentBounds(SKBitmap source)
        {
            int width = source.Width;
            int height = source.Height;
            int stepX = Math.Max(1, width / 32);
            int stepY = Math.Max(1, height / 32);

            bool RowIsBar(int y)
            {
                for (int x = 0; x < width; x += stepX)
                {
                    if (Luma(source.GetPixel(x, y)) > BarLuma) return false;
                }

                return true;
            }

            bool ColumnIsBar(int x)
            {
                for (int y = 0; y < height; y += stepY)
                {
                    if (Luma(source.GetPixel(x, y)) > BarLuma) return false;
                }

                return true;
            }

            int top = 0;
            while (top < height / 2 && RowIsBar(top)) top++;

            int bottom = height - 1;
            while (bottom > height / 2 && RowIsBar(bottom)) bottom--;

            int left = 0;
            while (left < width / 2 && ColumnIsBar(left)) left++;

            int right = width - 1;
            while (right > width / 2 && ColumnIsBar(right)) right--;

            return new SKRect(left, top, right + 1, bottom + 1);
        }

        private static double Luma(SKColor c) => (0.2126 * c.Red) + (0.7152 * c.Green) + (0.0722 * c.Blue);

        // AnalyzeFrame
        // Measures the frame once and reports everything the score is built from, so the pixels are
        // marshaled to managed memory a single time.
        internal static FrameQuality AnalyzeFrame(SKBitmap? analysis)
        {
            if (analysis == null)
            {
                return default;
            }

            var pixels = analysis.GetPixelSpan();
            if (pixels.IsEmpty)
            {
                return default;
            }

            int width = analysis.Width;
            int height = analysis.Height;
            int rowBytes = analysis.RowBytes;
            int pixelCount = width * height;

            // Only the luma plane is kept: the Laplacian below reads a pixel's neighbors, so it
            // needs the whole frame to hand. The color axes are reduced to running sums as they go,
            // because all that is wanted from them is a mean and a spread. Holding them as arrays
            // put three megabytes per frame on the large object heap for no gain.
            var luma = new double[pixelCount];
            double totalLuma = 0;
            double totalRg = 0;
            double totalYb = 0;
            double totalRgSquared = 0;
            double totalYbSquared = 0;

            for (int y = 0; y < height; y++)
            {
                int row = y * rowBytes;
                for (int x = 0; x < width; x++)
                {
                    int at = row + (x * 4);
                    double red = pixels[at];
                    double green = pixels[at + 1];
                    double blue = pixels[at + 2];

                    var value = (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
                    luma[(y * width) + x] = value;
                    totalLuma += value;

                    // Hasler and Susstrunk opponent axes: a frame with no color scores near zero
                    // on both, which is how a flat gray shot is told from a striking one.
                    var rg = Math.Abs(red - green);
                    var yb = Math.Abs((0.5 * (red + green)) - blue);

                    totalRg += rg;
                    totalYb += yb;
                    totalRgSquared += rg * rg;
                    totalYbSquared += yb * yb;
                }
            }

            double meanLuma = totalLuma / pixelCount;
            double brightness = meanLuma / 255.0;

            double lumaVariance = 0;
            for (int i = 0; i < pixelCount; i++)
            {
                var d = luma[i] - meanLuma;
                lumaVariance += d * d;
            }

            double contrast = Math.Sqrt(lumaVariance / pixelCount) / 255.0;

            double meanRg = totalRg / pixelCount;
            double meanYb = totalYb / pixelCount;
            double varRg = Math.Max(0, (totalRgSquared / pixelCount) - (meanRg * meanRg));
            double varYb = Math.Max(0, (totalYbSquared / pixelCount) - (meanYb * meanYb));

            double colorfulness =
                (Math.Sqrt(varRg + varYb)
                 + (0.3 * Math.Sqrt((meanRg * meanRg) + (meanYb * meanYb)))) / 255.0;

            // Laplacian energy overall, and separately in the bands a design is most likely to set
            // its text in, so a frame that leaves somewhere quiet for the title is preferred.
            int bandHeight = Math.Max(1, (int)(height * TextBandRatio));
            double total = 0;
            double topBand = 0;
            double bottomBand = 0;
            int count = 0;
            int topCount = 0;
            int bottomCount = 0;

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
                    double energy = lap * lap;

                    total += energy;
                    count++;

                    if (y < bandHeight)
                    {
                        topBand += energy;
                        topCount++;
                    }
                    else if (y >= height - bandHeight)
                    {
                        bottomBand += energy;
                        bottomCount++;
                    }
                }
            }

            double sharpness = count > 0 ? total / count : 0.0;
            double headroom = Headroom(
                sharpness,
                topCount > 0 ? topBand / topCount : 0.0,
                bottomCount > 0 ? bottomBand / bottomCount : 0.0);

            return new FrameQuality(brightness, contrast, sharpness, colorfulness, headroom);
        }

        // Headroom
        // How much quieter the calmer of the two text bands is than the frame as a whole. A busy
        // frame with one calm strip has somewhere for a title to go; a frame that is busy edge to
        // edge does not, and text over it has to fight the picture.
        private static double Headroom(double overall, double top, double bottom)
        {
            if (overall <= 0)
            {
                return 0.0;
            }

            var calmest = Math.Min(top, bottom);
            return Math.Clamp(1.0 - (calmest / overall), 0.0, 1.0);
        }

        // CalculateQualityScore
        // Weighs the measures against reference points rather than against a pass mark. Nothing here
        // saturates in the ordinary range, so frames rank against each other instead of tying at the
        // top, which is what the old brightness and sharpness thresholds did to almost every frame.
        internal static double CalculateQualityScore(in FrameQuality quality)
        {
            // Mid tones win: a crushed frame and a blown out one are both bad, and only the first
            // of those was ever penalized.
            var offset = quality.Brightness - IdealBrightness;
            var tone = Math.Exp(-(offset * offset) / (2 * BrightnessSpread * BrightnessSpread));

            var detail = Math.Min(Math.Log(1 + quality.Sharpness) / Math.Log(1 + SharpnessReference), 1.0);
            var contrast = Math.Min(quality.Contrast / ContrastReference, 1.0);
            var colorfulness = Math.Min(quality.Colorfulness / ColorfulnessReference, 1.0);

            return (ToneWeight * tone)
                + (DetailWeight * detail)
                + (ContrastWeight * contrast)
                + (ColorfulnessWeight * colorfulness)
                + (HeadroomWeight * quality.Headroom);
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

        private readonly record struct FrameCandidate(string Path, double Score);

        // FrameQuality
        // What one frame measures, before any of it is weighed into a score.
        internal readonly record struct FrameQuality(
            double Brightness,
            double Contrast,
            double Sharpness,
            double Colorfulness,
            double Headroom);
    }

    /// <summary>
    /// An extracted frame file and its quality score.
    /// </summary>
    public readonly record struct ExtractedFrame(string Path, double Score);
}
