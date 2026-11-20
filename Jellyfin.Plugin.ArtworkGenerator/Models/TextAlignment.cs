using System.ComponentModel;
using JPKribs.Jellyfin.Base;

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
        [Description(TranslationKeys.OptionDesignDefault)]
        Auto,

        /// <summary>Against the left of the safe area.</summary>
        [Description(TranslationKeys.ButtonArrowLeft)]
        Left,

        /// <summary>Centered between the two sides.</summary>
        [Description(TranslationKeys.OptionCenter)]
        Center,

        /// <summary>Against the right of the safe area.</summary>
        [Description(TranslationKeys.ButtonArrowRight)]
        Right
    }
}
