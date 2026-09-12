namespace Jellyfin.Plugin.EpisodePosterGenerator.Models
{
    /// <summary>
    /// How a framed poster fills its two edges with the title and the subtitle.
    /// </summary>
    public enum TextEdge
    {
        /// <summary>The top edge fills first: the title takes it, and a lone subtitle takes it instead.</summary>
        TopFirst,

        /// <summary>The bottom edge fills first: the title takes it, and a lone subtitle takes it instead.</summary>
        BottomFirst,

        /// <summary>The title is pinned to the top and the subtitle to the bottom, each edge left empty when its line is missing.</summary>
        AlwaysTop,

        /// <summary>The title is pinned to the bottom and the subtitle to the top, each edge left empty when its line is missing.</summary>
        AlwaysBottom
    }
}
