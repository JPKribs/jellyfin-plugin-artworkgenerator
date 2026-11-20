using System.ComponentModel;
using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    public enum PosterFill
    {
        [Description(TranslationKeys.Original)]
        Original,
        [Description(TranslationKeys.OptionFill)]
        Fill,
        [Description(TranslationKeys.OptionFit)]
        Fit
    }
}
