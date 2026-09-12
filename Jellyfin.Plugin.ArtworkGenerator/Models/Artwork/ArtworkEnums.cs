using System.ComponentModel;
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
        Movie,

        /// <summary>
        /// A video that stands on its own rather than belonging to a series: a music video, a home
        /// video, or anything else a library holds as a plain video.
        /// </summary>
        Video
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
        [Description("Original Title")]
        OriginalTitle,

        /// <summary>The series sort title.</summary>
        [Description("Sort Title")]
        SortTitle,

        /// <summary>The name of the series folder on disk.</summary>
        [Description("Folder Name")]
        FolderName
    }

    /// <summary>
    /// Where a generated logo takes its color from.
    /// </summary>
    public enum LogoColorSource
    {
        /// <summary>The configured color.</summary>
        [Description("Chosen Color")]
        Fixed,

        /// <summary>The dominant color of the top level poster: a series' own, or a film's.</summary>
        [Description("Top Level Poster")]
        SeriesPoster,

        /// <summary>The dominant color of the top level backdrop: a series' own, or a film's.</summary>
        [Description("Top Level Backdrop")]
        SeriesBackdrop
    }

    /// <summary>
    /// How a logo lays out a name with a subtitle, such as "Star Wars: Andor".
    /// </summary>
    public enum LogoSubtitleMode
    {
        /// <summary>The whole name, as written.</summary>
        [Description("Draw the whole name")]
        Keep,

        /// <summary>Only the part before the colon or dash.</summary>
        [Description("Title only")]
        TitleOnly,

        /// <summary>Only the part after the colon or dash.</summary>
        [Description("Subtitle only")]
        SubtitleOnly,

        /// <summary>The title large, with the subtitle small beneath it.</summary>
        [Description("Title large, subtitle small below")]
        TitleLarge,

        /// <summary>The subtitle large, with the title small above it.</summary>
        [Description("Subtitle large, title small above")]
        SubtitleLarge
    }

    /// <summary>
    /// What fills a logo's letters.
    /// </summary>
    public enum LogoFill
    {
        /// <summary>A solid color.</summary>
        [Description("Color")]
        Color,

        /// <summary>A frame from the series, as if the letters were cut out of a photo.</summary>
        [Description("Frame")]
        Photo
    }

    /// <summary>
    /// Helpers for reading an item kind.
    /// </summary>
    public static class ArtworkItemKinds
    {
        /// <summary>
        /// Whether the kind stands on its own rather than belonging to a series. A film and a plain
        /// video are both their own work: their own file is the source, their own name is the
        /// title, and they are assigned to a profile by their own id.
        /// </summary>
        public static bool IsStandalone(this ArtworkItemKind kind)
            => kind is ArtworkItemKind.Movie or ArtworkItemKind.Video;
    }
}
