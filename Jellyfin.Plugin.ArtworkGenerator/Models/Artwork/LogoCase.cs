using System.ComponentModel;
using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    /// <summary>
    /// How a logo's letters are cased, whatever case the name arrives in.
    /// </summary>
    public enum LogoCase
    {
        /// <summary>However the name is already written.</summary>
        [Description(TranslationKeys.OptionAsWritten)]
        AsWritten,

        /// <summary>Every letter capital.</summary>
        [Description(TranslationKeys.OptionUppercase)]
        Uppercase,

        /// <summary>Every letter small.</summary>
        [Description(TranslationKeys.OptionLowercase)]
        Lowercase,

        /// <summary>The first letter of each word capital.</summary>
        [Description(TranslationKeys.OptionTitleCase)]
        TitleCase
    }
}
