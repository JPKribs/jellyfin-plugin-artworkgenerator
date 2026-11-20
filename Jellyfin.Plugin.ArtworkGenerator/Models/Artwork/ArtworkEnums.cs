using System;
using System.ComponentModel;
using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    /// <summary>
    /// How many frames are scored from a video for each one that is kept.
    /// </summary>
    public enum SamplingRate
    {
        /// <summary>Fewer samples: faster, with less to choose from.</summary>
        Low,

        /// <summary>The standard number of samples.</summary>
        Medium,

        /// <summary>More samples: slower, with more to choose from.</summary>
        High
    }

    /// <summary>
    /// The kinds of library item the plugin can generate artwork for.
    /// </summary>
    public enum ArtworkItemKind
    {
        /// <summary>A TV series.</summary>
        [Description(TranslationKeys.Series)]
        Series,

        /// <summary>A season of a TV series.</summary>
        [Description(TranslationKeys.Season)]
        Season,

        /// <summary>A single episode.</summary>
        [Description(TranslationKeys.Episode)]
        Episode,

        /// <summary>A film.</summary>
        [Description(TranslationKeys.Movie)]
        Movie,

        /// <summary>
        /// A video that stands on its own rather than belonging to a series: a music video, a home
        /// video, or anything else a library holds as a plain video.
        /// </summary>
        [Description(TranslationKeys.Video)]
        Video,

        /// <summary>A collection, drawn from the videos it holds.</summary>
        [Description(TranslationKeys.LabelCollection)]
        Collection,

        /// <summary>A plain library folder, drawn from the videos beneath it.</summary>
        [Description(TranslationKeys.LabelFolder)]
        Folder,

        /// <summary>
        /// A whole library. Kept only so a configuration saved with library cells still loads. A
        /// library gets no generated artwork, and its stored cells are dropped on load.
        /// </summary>
        Library
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
        [Description(TranslationKeys.LabelTitle)]
        Title,

        /// <summary>The series' original-language title.</summary>
        [Description(TranslationKeys.LabelOriginalTitle)]
        OriginalTitle,

        /// <summary>The series sort title.</summary>
        [Description(TranslationKeys.LabelSortTitle)]
        SortTitle,

        /// <summary>The name of the series folder on disk.</summary>
        [Description(TranslationKeys.OptionFolderName)]
        FolderName
    }

    /// <summary>
    /// Where a generated logo takes its color from.
    /// </summary>
    public enum LogoColorSource
    {
        /// <summary>The configured color.</summary>
        [Description(TranslationKeys.OptionChosenColor)]
        Fixed,

        /// <summary>The dominant color of the top level poster: a series' own, or a film's.</summary>
        [Description(TranslationKeys.OptionTopLevelPoster)]
        SeriesPoster,

        /// <summary>The dominant color of the top level backdrop: a series' own, or a film's.</summary>
        [Description(TranslationKeys.OptionTopLevelBackdrop)]
        SeriesBackdrop
    }

    /// <summary>
    /// How a logo lays out a name with a subtitle, such as "Star Wars: Andor".
    /// </summary>
    public enum LogoSubtitleMode
    {
        /// <summary>The whole name, as written.</summary>
        [Description(TranslationKeys.OptionDrawTheWholeName)]
        Keep,

        /// <summary>Only the part before the colon or dash.</summary>
        [Description(TranslationKeys.OptionTitleOnly)]
        TitleOnly,

        /// <summary>Only the part after the colon or dash.</summary>
        [Description(TranslationKeys.OptionSubtitleOnly)]
        SubtitleOnly,

        /// <summary>The title large, with the subtitle small beneath it.</summary>
        [Description(TranslationKeys.OptionTitleLargeSubtitleSmallBelow)]
        TitleLarge,

        /// <summary>The subtitle large, with the title small above it.</summary>
        [Description(TranslationKeys.OptionSubtitleLargeTitleSmallAbove)]
        SubtitleLarge
    }

    /// <summary>
    /// What fills a logo's letters.
    /// </summary>
    public enum LogoFill
    {
        /// <summary>A solid color.</summary>
        [Description(TranslationKeys.OptionColor)]
        Color,

        /// <summary>A frame from the series, as if the letters were cut out of a photo.</summary>
        [Description(TranslationKeys.StyleFrame)]
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

        /// <summary>
        /// Whether the kind groups other items rather than being one, so its frames come from the
        /// videos it holds.
        /// </summary>
        public static bool IsGroup(this ArtworkItemKind kind)
            => kind is ArtworkItemKind.Collection or ArtworkItemKind.Folder;

        /// <summary>
        /// Whether the kind is assigned to a profile by its own id rather than by its series.
        /// </summary>
        public static bool IsAssignedByOwnId(this ArtworkItemKind kind)
            => kind.IsStandalone() || kind.IsGroup();
    }
}
