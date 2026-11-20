using System.ComponentModel;
using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    public enum Position
    {
        [Description(TranslationKeys.OptionTop)]
        Top,
        [Description(TranslationKeys.OptionCenter)]
        Center,
        [Description(TranslationKeys.OptionBottom)]
        Bottom
    }
}
