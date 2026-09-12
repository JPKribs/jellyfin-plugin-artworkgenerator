using System.ComponentModel.DataAnnotations;

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
        /// <summary>Gets or sets how far into each episode extraction begins.</summary>
        [Display(Name = "Extraction Start (%)", Description = "How far into each episode extraction begins.")]
        public float ExtractWindowStart { get; set; } = 20.0f;

        /// <summary>Gets or sets how far into each episode extraction stops.</summary>
        [Display(Name = "Extraction End (%)", Description = "How far into each episode extraction stops.")]
        public float ExtractWindowEnd { get; set; } = 80.0f;

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
    }
}
