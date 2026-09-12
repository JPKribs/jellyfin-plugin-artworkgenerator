using System;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
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
        Episode,

        /// <summary>A film.</summary>
        Movie
    }

    /// <summary>
    /// What a profile applies to. Tv is first so a profile saved before movies existed loads as a
    /// TV profile rather than silently taking over a library's films.
    /// </summary>
    public enum ProfileScope
    {
        /// <summary>Series, seasons, and episodes.</summary>
        Tv,

        /// <summary>Films only.</summary>
        Movies,

        /// <summary>Both.</summary>
        Both
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

    /// <summary>
    /// How a logo lays out a name with a subtitle, such as "Star Wars: Andor".
    /// </summary>
    public enum LogoSubtitleMode
    {
        /// <summary>The whole name, as written.</summary>
        Keep,

        /// <summary>Only the part before the colon or dash.</summary>
        TitleOnly,

        /// <summary>Only the part after the colon or dash.</summary>
        SubtitleOnly,

        /// <summary>The title large, with the subtitle small beneath it.</summary>
        TitleLarge,

        /// <summary>The subtitle large, with the title small above it.</summary>
        SubtitleLarge
    }

    /// <summary>
    /// What fills a logo's letters.
    /// </summary>
    public enum LogoFill
    {
        /// <summary>A solid colour.</summary>
        Color,

        /// <summary>A frame from the series, as if the letters were cut out of a photo.</summary>
        Photo
    }
}
