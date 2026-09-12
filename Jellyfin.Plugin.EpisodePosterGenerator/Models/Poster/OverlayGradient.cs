using System.ComponentModel;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Models
{
    public enum OverlayGradient
    {
        None,
        [Description("Left to Right")]
        LeftToRight,
        [Description("Bottom to Top")]
        BottomToTop,
        [Description("Top Left Corner to Bottom Right Corner")]
        TopLeftCornerToBottomRightCorner,
        [Description("Top Right Corner to Bottom Left Corner")]
        TopRightCornerToBottomLeftCorner
    }
}
