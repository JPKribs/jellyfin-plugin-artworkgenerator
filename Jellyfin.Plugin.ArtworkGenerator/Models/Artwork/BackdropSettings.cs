using System.ComponentModel.DataAnnotations;
using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    /// <summary>
    /// How a backdrop is framed. How its frame is taken from the video and cleaned up belongs to
    /// the server's frame extraction settings, which every image shares.
    /// </summary>
    public class BackdropSettings
    {
        /// <summary>Gets or sets the backdrop aspect ratio, such as 16:9.</summary>
        [Display(Name = TranslationKeys.AspectRatio, Description = TranslationKeys.AspectRatioHelp)]
        public string AspectRatio { get; set; } = "16:9";
    }
}
