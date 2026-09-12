using System.ComponentModel;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    /// <summary>
    /// How a logo's letters are cased, whatever case the name arrives in.
    /// </summary>
    public enum LogoCase
    {
        /// <summary>However the name is already written.</summary>
        [Description("As written")]
        AsWritten,

        /// <summary>Every letter capital.</summary>
        [Description("Uppercase")]
        Uppercase,

        /// <summary>Every letter small.</summary>
        [Description("Lowercase")]
        Lowercase,

        /// <summary>The first letter of each word capital.</summary>
        [Description("Title case")]
        TitleCase
    }
}
