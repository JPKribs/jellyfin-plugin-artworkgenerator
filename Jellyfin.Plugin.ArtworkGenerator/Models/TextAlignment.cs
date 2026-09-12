using System.ComponentModel;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    /// <summary>
    /// Which side of the safe area a design's block of text is pulled to.
    /// </summary>
    /// <remarks>
    /// The companion to <see cref="TextPosition"/>: that one decides the height the text sits at,
    /// this one the side. <see cref="Auto"/> means the placement the chosen design was drawn around,
    /// so adding this setting moved nothing until somebody picked a side.
    /// </remarks>
    public enum TextAlignment
    {
        /// <summary>Whichever side the chosen design naturally pulls its text to.</summary>
        [Description("Design default")]
        Auto,

        /// <summary>Against the left of the safe area.</summary>
        [Description("Left")]
        Left,

        /// <summary>Centered between the two sides.</summary>
        [Description("Center")]
        Center,

        /// <summary>Against the right of the safe area.</summary>
        [Description("Right")]
        Right
    }
}
