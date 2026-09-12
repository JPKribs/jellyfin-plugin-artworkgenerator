namespace Jellyfin.Plugin.EpisodePosterGenerator.Models
{
    /// <summary>
    /// Which edge of a framed poster the title sits in. The subtitle takes the other one.
    /// </summary>
    public enum TitleEdge
    {
        /// <summary>The title stays at the top while a subtitle holds the bottom, and moves down when there is none.</summary>
        Automatic,

        /// <summary>The title always sits in the top edge.</summary>
        Top,

        /// <summary>The title always sits in the bottom edge.</summary>
        Bottom
    }
}
