using System.ComponentModel.DataAnnotations;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    /// <summary>
    /// How a backdrop is framed. How its frame is taken from the video and cleaned up belongs to
    /// the server's frame extraction settings, which every image shares.
    /// </summary>
    public class BackdropSettings
    {
        /// <summary>Gets or sets the backdrop aspect ratio, such as 16:9.</summary>
        [Display(Name = "Aspect Ratio", Description = "The ratio the frame is cropped to, such as 16:9.")]
        public string AspectRatio { get; set; } = "16:9";

    }
}
