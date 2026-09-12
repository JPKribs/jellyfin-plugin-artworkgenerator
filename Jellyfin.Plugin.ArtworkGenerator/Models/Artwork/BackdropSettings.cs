using System.ComponentModel.DataAnnotations;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    /// <summary>
    /// How backdrops are extracted. Backdrops carry no design, so this is only framing and cleanup.
    /// </summary>
    public class BackdropSettings
    {
        /// <summary>Gets or sets the backdrop aspect ratio, such as 16:9.</summary>
        [Display(Name = "Aspect Ratio", Description = "The frame is cropped to this ratio, such as 16:9.")]
        public string AspectRatio { get; set; } = "16:9";

        /// <summary>Gets or sets a value indicating whether black bars are cropped away first.</summary>
        [Display(Name = "Detect Letterboxing", Description = "Detect and crop black bars from the frame first.")]
        public bool EnableLetterboxDetection { get; set; } = true;

        /// <summary>Gets or sets the brightness at or below which a pixel counts as black.</summary>
        [Display(Name = "Black Threshold", Description = "Brightness below which a pixel counts as black, 0 to 255.")]
        public int LetterboxBlackThreshold { get; set; } = 25;

        /// <summary>Gets or sets the percent of a row that must be black to count as letterboxing.</summary>
        [Display(Name = "Detection Confidence (%)", Description = "Percent of pixels that must be black to count as letterboxing, 50 to 100.")]
        public float LetterboxConfidence { get; set; } = 85.0f;

        /// <summary>Gets or sets the percent every extracted frame is brightened by.</summary>
        [Display(Name = "Brighten Frame (%)", Description = "Brightens every extracted frame by this percent. Default 0, which leaves the frame as it was extracted.")]
        public float BrightenHDR { get; set; }

        /// <summary>Gets or sets the percent of each episode skipped before frames are considered.</summary>
        [Display(Name = "Extraction Start (%)", Description = "Skip this percent of each episode before extracting, avoiding intros.")]
        public float ExtractWindowStart { get; set; } = 20.0f;

        /// <summary>Gets or sets the percent of each episode after which frames are no longer considered.</summary>
        [Display(Name = "Extraction End (%)", Description = "Stop extracting at this percent of each episode, avoiding credits.")]
        public float ExtractWindowEnd { get; set; } = 80.0f;
    }
}
