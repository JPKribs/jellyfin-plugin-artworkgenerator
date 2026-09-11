using System;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Models
{
    /// <summary>
    /// The kinds of library item the plugin can generate artwork for.
    /// </summary>
    public enum ArtworkItemKind
    {
        /// <summary>A TV series.</summary>
        Series,

        /// <summary>A season of a TV series.</summary>
        Season,

        /// <summary>A single episode.</summary>
        Episode
    }

    /// <summary>
    /// The Jellyfin image slots the plugin can fill. Each maps onto one Jellyfin image type.
    /// </summary>
    public enum ArtworkSlot
    {
        /// <summary>The item's main image. Portrait or landscape depending on the profile.</summary>
        Primary,

        /// <summary>A landscape card image.</summary>
        Thumb,

        /// <summary>A transparent title logo.</summary>
        Logo,

        /// <summary>A full-bleed background image, extracted with no design applied.</summary>
        Backdrop
    }

    /// <summary>
    /// The shape of a rendered poster.
    /// </summary>
    public enum ArtworkShape
    {
        /// <summary>Wider than tall, such as 16:9.</summary>
        Landscape,

        /// <summary>Taller than wide, such as 2:3.</summary>
        Portrait
    }

    /// <summary>
    /// The set of shapes a poster style can lay out.
    /// </summary>
    [Flags]
    public enum ArtworkShapes
    {
        /// <summary>No shapes.</summary>
        None = 0,

        /// <summary>Landscape posters.</summary>
        Landscape = 1,

        /// <summary>Portrait posters.</summary>
        Portrait = 2,

        /// <summary>Both shapes.</summary>
        All = Landscape | Portrait
    }

    /// <summary>
    /// Where a generated logo takes its text from.
    /// </summary>
    public enum LogoTitleSource
    {
        /// <summary>The series title as shown in the library.</summary>
        Title,

        /// <summary>The series' original-language title.</summary>
        OriginalTitle,

        /// <summary>The series sort title.</summary>
        SortTitle,

        /// <summary>The name of the series folder on disk.</summary>
        FolderName
    }

    /// <summary>
    /// Where a generated logo takes its colour from.
    /// </summary>
    public enum LogoColorSource
    {
        /// <summary>The configured colour.</summary>
        Fixed,

        /// <summary>The dominant colour of the series poster.</summary>
        SeriesPoster,

        /// <summary>The dominant colour of the series backdrop.</summary>
        SeriesBackdrop
    }
}
