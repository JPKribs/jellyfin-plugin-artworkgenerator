using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.EpisodePosterGenerator.Utilities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Models
{
    /// <summary>
    /// Everything a poster style needs to know about the item it is drawing, whatever kind of item it is.
    /// </summary>
    /// <remarks>
    /// Styles never ask whether they are drawing an episode. They draw a <see cref="Title"/>, a short
    /// <see cref="Code"/>, a <see cref="Number"/>, and a <see cref="Label"/>, and this class decides what
    /// those mean for an episode, a season, or a series. Supporting a new item kind means teaching this
    /// class, not every style.
    /// </remarks>
    public class ArtworkSubject
    {
        // "Season 2", "season 02", or a bare number: a name that only repeats the season number.
        private static readonly Regex GenericSeasonName = new(@"^(season\s*)?0*\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);


        public ArtworkSubject()
        {
            VideoMetadata = new VideoMetadata();
        }

        public ArtworkSubject(VideoMetadata videoMetadata)
        {
            VideoMetadata = videoMetadata;
        }

        public ArtworkItemKind Kind { get; set; } = ArtworkItemKind.Episode;

        public Guid ItemId { get; set; }

        public Guid SeriesId { get; set; }

        public string? SeriesName { get; set; }

        public string? OriginalTitle { get; set; }

        public string? SortTitle { get; set; }

        public string? FolderName { get; set; }

        public int? ProductionYear { get; set; }

        public string? SeasonName { get; set; }

        public int? SeasonNumber { get; set; }

        /// <summary>Gets or sets the number of numbered seasons in the series, when known.</summary>
        public int? SeasonCount { get; set; }

        public string? EpisodeName { get; set; }

        public int? EpisodeNumberStart { get; set; }

        public int? EpisodeNumberEnd { get; set; }

        /// <summary>Gets or sets the number of episodes in the season, when known.</summary>
        public int? SeasonEpisodeCount { get; set; }

        public VideoMetadata VideoMetadata { get; set; }

        /// <summary>
        /// Gets the primary line: an episode's name, a series' name, and a season's own name when
        /// it has one. An item with no name of its own is headlined by its subtitle instead, so the
        /// one thing it has to say is said once, in the larger type.
        /// </summary>
        public string? Title => OwnName ?? (Promoted ? Subtitle : null);

        /// <summary>
        /// Gets the secondary line: an episode's season and episode, or a season's number. Empty
        /// once it has been promoted to the primary line, so it is never drawn twice.
        /// </summary>
        public string Label => Promoted ? string.Empty : Subtitle;

        /// <summary>
        /// Gets or sets a value indicating whether the design draws a primary line at all. Set at
        /// render time, since it depends on the design rather than the item: a design showing only
        /// the subtitle keeps it as the subtitle, with nothing to promote it to.
        /// </summary>
        [JsonIgnore]
        public bool TitleShown { get; set; } = true;

        // The item's own name, before anything is promoted into its place.
        private string? OwnName => Kind switch
        {
            ArtworkItemKind.Episode => EpisodeName,
            ArtworkItemKind.Season => HasCustomSeasonName ? SeasonName : null,
            _ => SeriesName
        };

        // The secondary text this item would carry.
        private string Subtitle => Kind switch
        {
            ArtworkItemKind.Episode => EpisodeCodeUtils.FormatFullText(SeasonNumber ?? 0, EpisodeNumberStart ?? 0, true, true),
            ArtworkItemKind.Season => SeasonLabel,
            _ => string.Empty
        };

        // An item with nothing of its own promotes its subtitle, provided the design has a primary
        // line to promote it into.
        private bool Promoted => TitleShown && string.IsNullOrWhiteSpace(OwnName) && Subtitle.Length > 0;

        /// <summary>
        /// Gets a value indicating whether the season carries a name of its own rather than a
        /// numbered one, such as "The Crown Jewels" instead of "Season 2".
        /// </summary>
        public bool HasCustomSeasonName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(SeasonName))
                {
                    return false;
                }

                var name = SeasonName.Trim();
                return !GenericSeasonName.IsMatch(name);
            }
        }

        /// <summary>
        /// Gets the number a numeric style features: the episode number, or the season number.
        /// A series has none.
        /// </summary>
        public int? Number => Kind switch
        {
            ArtworkItemKind.Episode => EpisodeNumberStart,
            ArtworkItemKind.Season => SeasonNumber,
            _ => null
        };

        /// <summary>
        /// Gets the compact code: S01E05 for an episode, S01 for a season. A series has none: its
        /// name is its whole identity, so a series poster carries no season count or year.
        /// </summary>
        public string Code => Promoted ? string.Empty : Kind switch
        {
            ArtworkItemKind.Episode => EpisodeCodeUtils.FormatEpisodeCode(SeasonNumber ?? 0, EpisodeNumberStart ?? 0),
            ArtworkItemKind.Season => SeasonNumber.HasValue ? EpisodeCodeUtils.FormatSeasonCode(SeasonNumber.Value) : string.Empty,
            _ => string.Empty
        };

        /// <summary>
        /// Gets the numbers joined by a bullet in the compact number line, such as 12 • 7. Only an
        /// episode has more than one; other kinds draw <see cref="Label"/> instead.
        /// </summary>
        public IReadOnlyList<int> NumberParts => Kind == ArtworkItemKind.Episode
            ? new[] { SeasonNumber ?? 0, EpisodeNumberStart ?? 0 }
            : Array.Empty<int>();

        /// <summary>Gets the position along a progress bar: the episode within its season, or the season within its series.</summary>
        public int? ProgressPosition => Number;

        /// <summary>Gets the length of the progress bar, when known.</summary>
        public int? ProgressTotal => Kind switch
        {
            ArtworkItemKind.Episode => SeasonEpisodeCount,
            ArtworkItemKind.Season => SeasonCount,
            _ => null
        };

        /// <summary>Gets the text describing <see cref="ProgressPosition"/>, such as 7 OF 10.</summary>
        public string ProgressText
        {
            get
            {
                var position = ProgressPosition;
                var total = ProgressTotal;

                if (position.HasValue && total.HasValue)
                {
                    return string.Format(CultureInfo.InvariantCulture, "{0} OF {1}", position.Value, total.Value);
                }

                return Kind switch
                {
                    ArtworkItemKind.Episode when position.HasValue => string.Format(CultureInfo.InvariantCulture, "EPISODE {0}", position.Value),
                    ArtworkItemKind.Season when position.HasValue => string.Format(CultureInfo.InvariantCulture, "SEASON {0}", position.Value),
                    _ => Label
                };
            }
        }

        // A season's subtitle is always its number, so a named season shows its name as the
        // headline and its number beneath, rather than the name twice.
        private string SeasonLabel
        {
            get
            {
                if (SeasonNumber.HasValue)
                {
                    return string.Format(CultureInfo.InvariantCulture, "SEASON {0}", SeasonNumber.Value);
                }

                return string.IsNullOrWhiteSpace(SeasonName) ? string.Empty : SeasonName.ToUpperInvariant();
            }
        }

        /// <summary>
        /// Gets a value indicating whether a cutout style punches out the title itself. A series has
        /// no code or number, so its name becomes the cutout and is not drawn a second time.
        /// </summary>
        public bool CutoutIsTitle => Kind == ArtworkItemKind.Series || Promoted;

        /// <summary>
        /// Returns the text a cutout style punches out: the featured number spelled out when the
        /// design asks for that and the item has one, otherwise the item's own line, and otherwise
        /// the <see cref="Code"/>. The spelled-out number comes first, since an item named after
        /// nothing but its number is exactly where a numeral belongs.
        /// </summary>
        public string CutoutText(CutoutType type)
        {
            if (type == CutoutType.Text && Number.HasValue)
            {
                return EpisodeCodeUtils.FormatEpisodeText(CutoutType.Text, 0, Number.Value);
            }

            if (CutoutIsTitle)
            {
                return (Title ?? string.Empty).ToUpperInvariant();
            }

            return Code;
        }

        // FromItem
        // Builds the subject for any supported library item.
        public static ArtworkSubject FromItem(BaseItem item) => item switch
        {
            Episode episode => FromEpisode(episode),
            Season season => FromSeason(season),
            Series series => FromSeries(series),
            _ => throw new ArgumentException("Artwork can only be generated for series, seasons, and episodes.", nameof(item))
        };

        // FromEpisode
        // Builds the subject for an episode.
        public static ArtworkSubject FromEpisode(Episode episode)
        {
            ArgumentNullException.ThrowIfNull(episode);

            var series = episode.Series;
            var season = episode.Season;

            var subject = new ArtworkSubject(VideoMetadata.Create(episode, series, episode))
            {
                Kind = ArtworkItemKind.Episode,
                ItemId = episode.Id,
                EpisodeName = episode.Name,
                EpisodeNumberStart = episode.IndexNumber,
                EpisodeNumberEnd = episode.IndexNumberEnd ?? episode.IndexNumber,
                SeasonNumber = GetSeasonNumber(episode),
                SeasonName = season?.Name ?? episode.SeasonName,
                SeasonEpisodeCount = CountEpisodes(season)
            };

            ApplySeries(subject, series, episode.SeriesName, episode.SeriesId);
            return subject;
        }

        // FromSeason
        // Builds the subject for a season. The video source is its first playable episode.
        public static ArtworkSubject FromSeason(Season season)
        {
            ArgumentNullException.ThrowIfNull(season);

            var series = season.Series;
            var sources = ArtworkSources.GetPlayableEpisodes(season);
            var source = sources.Count > 0 ? sources[0] : null;

            var subject = new ArtworkSubject(VideoMetadata.Create(season, series, source))
            {
                Kind = ArtworkItemKind.Season,
                ItemId = season.Id,
                SeasonNumber = season.IndexNumber,
                SeasonName = season.Name,
                SeasonEpisodeCount = CountEpisodes(season)
            };

            ApplySeries(subject, series, season.SeriesName, season.SeriesId);
            return subject;
        }

        // FromSeries
        // Builds the subject for a series. The video source is its first playable episode.
        public static ArtworkSubject FromSeries(Series series)
        {
            ArgumentNullException.ThrowIfNull(series);

            var sources = ArtworkSources.GetPlayableEpisodes(series);
            var source = sources.Count > 0 ? sources[0] : null;

            var subject = new ArtworkSubject(VideoMetadata.Create(series, series, source))
            {
                Kind = ArtworkItemKind.Series,
                ItemId = series.Id
            };

            ApplySeries(subject, series, series.Name, series.Id);
            return subject;
        }

        // ApplySeries
        // Fills the series-level fields shared by every kind of subject.
        private static void ApplySeries(ArtworkSubject subject, Series? series, string? fallbackName, Guid fallbackId)
        {
            subject.SeriesId = series?.Id ?? fallbackId;
            subject.SeriesName = series?.Name ?? fallbackName;
            subject.OriginalTitle = series?.OriginalTitle;
            subject.SortTitle = series?.SortName;
            subject.ProductionYear = series?.ProductionYear;
            subject.SeasonCount = CountSeasons(series);

            var path = series?.Path;
            if (!string.IsNullOrEmpty(path))
            {
                subject.FolderName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
        }

        // CountEpisodes
        // Counts the non-virtual episodes in a season, or null when unknown.
        private static int? CountEpisodes(Season? season)
        {
            try
            {
                var count = season?.Children?.OfType<Episode>().Count(e => !e.IsVirtualItem) ?? 0;
                return count > 0 ? count : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        // CountSeasons
        // Counts the numbered, non-virtual seasons of a series, or null when unknown. Specials are
        // not a season in the sense a "3 seasons" label means.
        private static int? CountSeasons(Series? series)
        {
            try
            {
                var count = series?.Children?.OfType<Season>().Count(s => !s.IsVirtualItem && (s.IndexNumber ?? 0) > 0) ?? 0;
                return count > 0 ? count : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        // GetSeasonNumber
        // Retrieves the season number from the episode or its parent season.
        private static int? GetSeasonNumber(Episode episode)
        {
            if (episode.ParentIndexNumber.HasValue)
            {
                return episode.ParentIndexNumber.Value;
            }

            var season = episode.Season;
            if (season?.IndexNumber.HasValue == true)
            {
                return season.IndexNumber.Value;
            }

            return episode.AiredSeasonNumber;
        }
    }
}
