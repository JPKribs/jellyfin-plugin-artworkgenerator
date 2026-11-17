using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    /// <summary>
    /// How frames are pulled out of a video and cleaned up before anything is drawn on them.
    /// </summary>
    /// <remarks>
    /// These belong to the server rather than to a design. Every design and every backdrop used to
    /// carry its own copy, which meant the same decision was made in several places and could
    /// disagree with itself. They are set once here and used for every extracted frame.
    /// </remarks>
    public class FrameExtractionSettings
    {
        /// <summary>The default for <see cref="SamplesPerFrame"/>, what Medium used.</summary>
        public const int DefaultSamplesPerFrame = 4;

        /// <summary>The default for <see cref="MinimumSamplesPerVideo"/>, what Medium used.</summary>
        public const int DefaultMinimumSamplesPerVideo = 8;

        /// <summary>The default for <see cref="MaximumSamplesPerVideo"/>, what Medium used.</summary>
        public const int DefaultMaximumSamplesPerVideo = 48;

        /// <summary>The most <see cref="SamplesPerFrame"/> accepted.</summary>
        public const int MaxSamplesPerFrame = 8;

        /// <summary>
        /// The most samples one video's extraction may take. The frame pool starts each pass over a
        /// video this many steps further along its walk, so a run longer than this would revisit
        /// timestamps an earlier pass already took.
        /// </summary>
        public const int MaxSamplesPerVideo = 64;

        /// <summary>The default for <see cref="ParallelExtractions"/>.</summary>
        public const int DefaultParallelExtractions = 2;

        /// <summary>The most extractions that may run at once, whatever is configured.</summary>
        public const int MaxParallelExtractions = 8;

        /// <summary>Gets or sets how far into each episode extraction begins.</summary>
        [Display(Name = "Extraction Start (%)", Description = "How far into each episode extraction begins.")]
        public float ExtractWindowStart { get; set; } = 20.0f;

        /// <summary>Gets or sets how far into each episode extraction stops.</summary>
        [Display(Name = "Extraction End (%)", Description = "How far into each episode extraction stops.")]
        public float ExtractWindowEnd { get; set; } = 80.0f;

        /// <summary>Gets or sets a value indicating whether frames are kept out of the item's media segments.</summary>
        [Display(Name = "Avoid Media Segments", Description = "Take no frame from inside the media segments ticked below. Segments come from a media segment provider, so an item without any is unaffected.")]
        public bool AvoidMediaSegments { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether intro segments are avoided.</summary>
        [Display(Name = "Intros", Description = "Opening titles.")]
        public bool AvoidIntros { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether outro segments are avoided.</summary>
        [Display(Name = "Outros", Description = "Closing credits.")]
        public bool AvoidOutros { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether recap segments are avoided.</summary>
        [Display(Name = "Recaps", Description = "Footage from earlier episodes.")]
        public bool AvoidRecaps { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether preview segments are avoided.</summary>
        [Display(Name = "Previews", Description = "Footage from the next episode.")]
        public bool AvoidPreviews { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether commercial segments are avoided.</summary>
        [Display(Name = "Commercials", Description = "Advertising breaks.")]
        public bool AvoidCommercials { get; set; } = true;

        /// <summary>Gets or sets the percent every extracted frame is brightened by.</summary>
        [Display(Name = "Brighten Frame (%)", Description = "How much every extracted frame is brightened.")]
        public float BrightenFrame { get; set; }

        /// <summary>Gets or sets a value indicating whether black bars are cropped away first.</summary>
        [Display(Name = "Detect Letterboxing", Description = "Detect and crop black bars from the frame first.")]
        public bool EnableLetterboxDetection { get; set; } = true;

        /// <summary>Gets or sets the brightness at or below which a pixel counts as black.</summary>
        [Display(Name = "Black Threshold", Description = "Brightness below which a pixel counts as black.")]
        public int LetterboxBlackThreshold { get; set; } = 25;

        /// <summary>Gets or sets the percent of a row that must be black to count as letterboxing.</summary>
        [Display(Name = "Detection Confidence (%)", Description = "Percent of pixels that must be black to count as letterboxing.")]
        public float LetterboxConfidence { get; set; } = 85.0f;

        /// <summary>Gets or sets how many frames are scored for each one kept.</summary>
        [Display(Name = "Samples Per Frame", Description = "How many frames are scored for each one kept, 1 to 8. Higher picks better frames but takes longer, since every sample is its own extraction. 1 keeps every frame it takes, with no choosing.")]
        public int SamplesPerFrame { get; set; } = DefaultSamplesPerFrame;

        /// <summary>Gets or sets the fewest frames scored from a single video.</summary>
        [Display(Name = "Minimum Samples Per Video", Description = "The fewest frames scored from a film, video, or episode on its own, 1 to 64, so even a single image is chosen from several. A series, season, collection, or folder compares its videos' frames with each other instead, so it is not held to this.")]
        public int MinimumSamplesPerVideo { get; set; } = DefaultMinimumSamplesPerVideo;

        /// <summary>Gets or sets the most frames scored from one video in one go.</summary>
        [Display(Name = "Maximum Samples Per Video", Description = "The most frames scored from one video in one go, 1 to 64. It caps how long a film with many choices takes.")]
        public int MaximumSamplesPerVideo { get; set; } = DefaultMaximumSamplesPerVideo;

        /// <summary>
        /// Gets or sets the sampling preset a configuration saved before the three sampling numbers
        /// carries. It is read so the numbers can be set from it, and never written back.
        /// </summary>
        [JsonIgnore]
        public SamplingRate? SamplingRate { get; set; }

        /// <summary>Gets or sets the most videos a series, season, collection, folder, or library takes frames from, or 0 for all.</summary>
        [Display(Name = "Videos Per Parent", Description = "Most videos a series, season, collection, or folder takes frames from, or 0 for all. Lower is faster on network storage.")]
        public int VideosPerParent { get; set; } = ArtworkSources.DefaultVideosPerParent;

        /// <summary>Gets or sets how many frames are extracted at once.</summary>
        [Display(Name = "Parallel Extractions", Description = "How many frames are extracted at once, 1 to 8. Higher is faster on local storage. Lower it on slow network storage.")]
        public int ParallelExtractions { get; set; } = DefaultParallelExtractions;

        /// <summary>Gets or sets a value indicating whether image types draw from overlapping frames.</summary>
        [Display(Name = "Reuse Samples", Description = "Let an item's poster, thumb, backdrop, and logo draw from overlapping frames. Off gives every image type its own frames, at the cost of extracting more.")]
        public bool ReuseSamples { get; set; } = true;
    
        /// <summary>The samples per frame, minimum, and maximum each old preset stood for.</summary>
        public static (int PerFrame, int Minimum, int Maximum) Preset(SamplingRate rate) => rate switch
        {
            Models.SamplingRate.Low => (2, 4, 24),
            Models.SamplingRate.High => (8, 16, 64),
            _ => (DefaultSamplesPerFrame, DefaultMinimumSamplesPerVideo, DefaultMaximumSamplesPerVideo)
        };

        /// <summary>
        /// Sets the three sampling numbers from a preset an older configuration saved, so upgrading
        /// changes no one's sampling, and drops the preset. Returns true when there was one.
        /// </summary>
        public bool UpgradeLegacySampling()
        {
            if (SamplingRate is not { } rate)
            {
                return false;
            }

            (SamplesPerFrame, MinimumSamplesPerVideo, MaximumSamplesPerVideo) = Preset(rate);
            SamplingRate = null;
            return true;
        }

        /// <summary>Tells the XML serializer never to write the old preset back.</summary>
        public bool ShouldSerializeSamplingRate() => false;

        /// <summary>
        /// The sampling numbers in range: samples per frame from 1 to 8, and a minimum no larger than
        /// the maximum, both from 1 to 64.
        /// </summary>
        public (int PerFrame, int Minimum, int Maximum) Sampling()
        {
            var maximum = Math.Clamp(MaximumSamplesPerVideo, 1, MaxSamplesPerVideo);
            return (
                Math.Clamp(SamplesPerFrame, 1, MaxSamplesPerFrame),
                Math.Clamp(MinimumSamplesPerVideo, 1, maximum),
                maximum);
        }
}
}
