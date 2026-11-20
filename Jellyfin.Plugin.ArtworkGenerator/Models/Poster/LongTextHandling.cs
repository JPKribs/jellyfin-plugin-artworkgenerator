using System.ComponentModel;
using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    /// <summary>
    /// How a poster handles an episode title that does not fit its text area.
    /// </summary>
    public enum LongTextHandling
    {
        /// <summary>Trim the title with an ellipsis.</summary>
        [Description(TranslationKeys.OptionEllipsis)]
        Ellipsis,

        /// <summary>
        /// Shorten the title in stages: the text before a divider, then the first
        /// sentence, then word initials with periods such as "L.O.T.R.".
        /// </summary>
        [Description(TranslationKeys.OptionAbbreviate)]
        Abbreviate,

        /// <summary>Do not draw the title at all.</summary>
        [Description(TranslationKeys.OptionDropName)]
        DropName
    }
}
