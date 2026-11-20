using System.ComponentModel;
using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    public enum Alignment
    {
        [Description(TranslationKeys.ButtonArrowLeft)]
        Left,
        [Description(TranslationKeys.OptionCenter)]
        Center,
        [Description(TranslationKeys.ButtonArrowRight)]
        Right
    }
}
