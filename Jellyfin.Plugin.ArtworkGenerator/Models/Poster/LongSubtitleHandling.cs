using System.ComponentModel;
using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    /// <summary>
    /// How a design handles a subtitle that does not fit the width it has.
    /// </summary>
    /// <remarks>
    /// A subtitle is short by nature, so it only runs out of room on a narrow poster, where
    /// "SEASON 12 • EPISODE 7" has to become something. The choice is what it becomes.
    /// </remarks>
    public enum LongSubtitleHandling
    {
        /// <summary>Say the same thing in its short form, such as S12E07.</summary>
        [Description(TranslationKeys.OptionShortCode)]
        ShortCode,

        /// <summary>Keep the words and set them smaller.</summary>
        [Description(TranslationKeys.OptionShrinkToFit)]
        Shrink,

        /// <summary>Trim the words with an ellipsis.</summary>
        [Description(TranslationKeys.OptionEllipsis)]
        Ellipsis
    }
}
