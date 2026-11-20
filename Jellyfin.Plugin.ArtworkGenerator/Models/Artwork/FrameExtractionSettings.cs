using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using JPKribs.Jellyfin.Base;

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
        [Display(Name = TranslationKeys.LabelExtractionStartPercent, Description = TranslationKeys.ExtractWindowStartHelp)]
        public float ExtractWindowStart { get; set; } = 20.0f;

        /// <summary>Gets or sets how far into each episode extraction stops.</summary>
        [Display(Name = TranslationKeys.LabelExtractionEndPercent, Description = TranslationKeys.ExtractWindowEndHelp)]
        public float ExtractWindowEnd { get; set; } = 80.0f;

        /// <summary>Gets or sets a value indicating whether frames are kept out of the item's media segments.</summary>
        [Display(Name = TranslationKeys.LabelAvoidMediaSegments, Description = TranslationKeys.AvoidMediaSegmentsHelp)]
        public bool AvoidMediaSegments { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether intro segments are avoided.</summary>
        [Display(Name = TranslationKeys.LabelIntros, Description = TranslationKeys.AvoidIntrosHelp)]
        public bool AvoidIntros { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether outro segments are avoided.</summary>
        [Display(Name = TranslationKeys.LabelOutros, Description = TranslationKeys.AvoidOutrosHelp)]
        public bool AvoidOutros { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether recap segments are avoided.</summary>
        [Display(Name = TranslationKeys.LabelRecaps, Description = TranslationKeys.AvoidRecapsHelp)]
        public bool AvoidRecaps { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether preview segments are avoided.</summary>
        [Display(Name = TranslationKeys.LabelPreviews, Description = TranslationKeys.AvoidPreviewsHelp)]
        public bool AvoidPreviews { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether commercial segments are avoided.</summary>
        [Display(Name = TranslationKeys.LabelCommercials, Description = TranslationKeys.AvoidCommercialsHelp)]
        public bool AvoidCommercials { get; set; } = true;

        /// <summary>Gets or sets the percent every extracted frame is brightened by.</summary>
        [Display(Name = TranslationKeys.LabelBrightenFramePercent, Description = TranslationKeys.BrightenFrameHelp)]
        public float BrightenFrame { get; set; }

        /// <summary>Gets or sets a value indicating whether black bars are cropped away first.</summary>
        [Display(Name = TranslationKeys.LabelDetectLetterboxing, Description = TranslationKeys.EnableLetterboxDetectionHelp)]
        public bool EnableLetterboxDetection { get; set; } = true;

        /// <summary>Gets or sets the brightness at or below which a pixel counts as black.</summary>
        [Display(Name = TranslationKeys.LabelBlackThreshold, Description = TranslationKeys.LetterboxBlackThresholdHelp)]
        public int LetterboxBlackThreshold { get; set; } = 25;

        /// <summary>Gets or sets the percent of a row that must be black to count as letterboxing.</summary>
        [Display(Name = TranslationKeys.LabelDetectionConfidencePercent, Description = TranslationKeys.LetterboxConfidenceHelp)]
        public float LetterboxConfidence { get; set; } = 85.0f;

        /// <summary>Gets or sets how many frames are scored for each one kept.</summary>
        [Display(Name = TranslationKeys.LabelSamplesPerFrame, Description = TranslationKeys.SamplesPerFrameHelp)]
        public int SamplesPerFrame { get; set; } = DefaultSamplesPerFrame;

        /// <summary>Gets or sets the fewest frames scored from a single video.</summary>
        [Display(Name = TranslationKeys.LabelMinimumSamplesPerVideo, Description = TranslationKeys.MinimumSamplesPerVideoHelp)]
        public int MinimumSamplesPerVideo { get; set; } = DefaultMinimumSamplesPerVideo;

        /// <summary>Gets or sets the most frames scored from one video in one go.</summary>
        [Display(Name = TranslationKeys.LabelMaximumSamplesPerVideo, Description = TranslationKeys.MaximumSamplesPerVideoHelp)]
        public int MaximumSamplesPerVideo { get; set; } = DefaultMaximumSamplesPerVideo;

        /// <summary>
        /// Gets or sets the sampling preset a configuration saved before the three sampling numbers
        /// carries. It is read so the numbers can be set from it, and never written back.
        /// </summary>
        [JsonIgnore]
        public SamplingRate? SamplingRate { get; set; }

        /// <summary>Gets or sets the most videos a series, season, collection, folder, or library takes frames from, or 0 for all.</summary>
        [Display(Name = TranslationKeys.LabelVideosPerParent, Description = TranslationKeys.VideosPerParentHelp)]
        public int VideosPerParent { get; set; } = ArtworkSources.DefaultVideosPerParent;

        /// <summary>Gets or sets how many frames are extracted at once.</summary>
        [Display(Name = TranslationKeys.LabelParallelExtractions, Description = TranslationKeys.ParallelExtractionsHelp)]
        public int ParallelExtractions { get; set; } = DefaultParallelExtractions;

        /// <summary>Gets or sets a value indicating whether image types draw from overlapping frames.</summary>
        [Display(Name = TranslationKeys.LabelReuseSamples, Description = TranslationKeys.ReuseSamplesHelp)]
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
