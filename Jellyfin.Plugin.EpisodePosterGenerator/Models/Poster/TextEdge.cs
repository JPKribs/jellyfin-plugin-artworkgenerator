using System.ComponentModel;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Models
{
    /// <summary>
    /// How a framed poster fills its two edges with the title and the subtitle.
    /// </summary>
    public enum TextEdge
    {
        /// <summary>The top edge fills first: the title takes it, and a lone subtitle takes it instead.</summary>
        [Description("Top edge first")]
        TopFirst,

        /// <summary>The bottom edge fills first: the title takes it, and a lone subtitle takes it instead.</summary>
        [Description("Bottom edge first")]
        BottomFirst,

        /// <summary>The title is pinned to the top and the subtitle to the bottom, each edge left empty when its line is missing.</summary>
        [Description("Title always top")]
        AlwaysTop,

        /// <summary>The title is pinned to the bottom and the subtitle to the top, each edge left empty when its line is missing.</summary>
        [Description("Title always bottom")]
        AlwaysBottom
    }
}
