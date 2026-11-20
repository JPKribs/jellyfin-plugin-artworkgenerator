using System.ComponentModel;
using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    public enum OverlayGradient
    {
        [Description(TranslationKeys.None)]
        None,
        [Description(TranslationKeys.ViewLeftToRight)]
        LeftToRight,
        [Description(TranslationKeys.OptionBottomToTop)]
        BottomToTop,
        [Description(TranslationKeys.OptionTopLeftCornerToBottomRightCorner)]
        TopLeftCornerToBottomRightCorner,
        [Description(TranslationKeys.OptionTopRightCornerToBottomLeftCorner)]
        TopRightCornerToBottomLeftCorner
    }
}
