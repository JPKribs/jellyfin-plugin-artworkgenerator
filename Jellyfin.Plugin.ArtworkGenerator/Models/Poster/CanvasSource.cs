using System.ComponentModel;
using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    // CanvasSource
    // Determines what image is used as the poster canvas background.
    public enum CanvasSource
    {
        // No background - a transparent canvas is used (overlay/text only).
        [Description(TranslationKeys.OptionNoBackground)]
        None,

        // Extract a representative frame from the episode video.
        [Description(TranslationKeys.OptionExtractFrameFromVideo)]
        Extract,

        // Use the parent series' backdrop image as the canvas.
        [Description(TranslationKeys.OptionUseSeriesBackdrop)]
        SeriesBackdrop,

        // Several frames tiled into one canvas, like a wall of photographs.
        [Description(TranslationKeys.OptionGridOfExtractedFrames)]
        Grid
    }
}
