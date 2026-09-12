using System.ComponentModel;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    // CanvasSource
    // Determines what image is used as the poster canvas background.
    public enum CanvasSource
    {
        // No background - a transparent canvas is used (overlay/text only).
        [Description("No Background")]
        None,

        // Extract a representative frame from the episode video.
        [Description("Extract Frame from Video")]
        Extract,

        // Use the parent series' backdrop image as the canvas.
        [Description("Use Series Backdrop")]
        SeriesBackdrop,

        // Several frames tiled into one canvas, like a wall of photographs.
        [Description("Grid of Frames")]
        Grid
    }
}
