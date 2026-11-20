using System.ComponentModel;
using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    public enum CutoutType
    {
        [Description(TranslationKeys.LabelTypeText)]
        Text,
        [Description(TranslationKeys.OptionCode)]
        Code
    }
}
