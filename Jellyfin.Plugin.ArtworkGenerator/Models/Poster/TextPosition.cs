using System.ComponentModel;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    /// <summary>
    /// Where a design puts its block of text inside the safe area.
    /// </summary>
    /// <remarks>
    /// <see cref="Auto"/> exists so this setting can be added without moving anything: a design
    /// that has always packed its text against the bottom keeps doing so, and one built around
    /// centered text keeps that, until somebody actually picks a side. Designs whose text is part of
    /// the composition rather than a block on top of it — a sash, a sideways title, lettering cut
    /// out of the image — do not offer this setting at all.
    /// </remarks>
    public enum TextPosition
    {
        /// <summary>Wherever the chosen design naturally puts its text.</summary>
        [Description("Design default")]
        Auto,

        /// <summary>Against the top of the safe area.</summary>
        [Description("Top")]
        Top,

        /// <summary>Centered between the top and bottom of the safe area.</summary>
        [Description("Center")]
        Center,

        /// <summary>Against the bottom of the safe area.</summary>
        [Description("Bottom")]
        Bottom
    }
}
