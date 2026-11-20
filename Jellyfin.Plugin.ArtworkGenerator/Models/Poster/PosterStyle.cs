using System.ComponentModel;
using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    public enum PosterStyle
    {
        [Description(TranslationKeys.StyleStandard)]
        Standard,
        [Description(TranslationKeys.StyleCutout)]
        Cutout,
        [Description(TranslationKeys.StyleNumeral)]
        Numeral,
        [Description(TranslationKeys.Logo)]
        Logo,
        [Description(TranslationKeys.StyleFrame)]
        Frame,
        [Description(TranslationKeys.StyleBrush)]
        Brush,
        [Description(TranslationKeys.StyleSplit)]
        Split,
        [Description(TranslationKeys.StyleFrostedGlass)]
        FrostedGlass,
        [Description(TranslationKeys.StyleFade)]
        Fade,
        [Description(TranslationKeys.StyleTimeline)]
        Timeline,
        [Description(TranslationKeys.StyleStriped)]
        Striped,
        [Description(TranslationKeys.StyleBloom)]
        Bloom
    }
}
