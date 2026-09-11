using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
        /// Gets the headline: the episode name for an episode, and the series name for a season or
        /// series, whose own identity is carried by <see cref="Label"/>.
        /// </summary>
        public string? Title => Kind == ArtworkItemKind.Episode ? EpisodeName : SeriesName;

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
        public string Code => Kind switch
        {
            ArtworkItemKind.Episode => EpisodeCodeUtils.FormatEpisodeCode(SeasonNumber ?? 0, EpisodeNumberStart ?? 0),
            ArtworkItemKind.Season => SeasonNumber.HasValue ? EpisodeCodeUtils.FormatSeasonCode(SeasonNumber.Value) : string.Empty,
            _ => string.Empty
        };

        /// <summary>
        /// Gets the spelled-out identity line: SEASON 1 • EPISODE 5, or SEASON 1 (or the season's own
        /// name). Empty for a series.
        /// </summary>
        public string Label => Kind switch
        {
            ArtworkItemKind.Episode => EpisodeCodeUtils.FormatFullText(SeasonNumber ?? 0, EpisodeNumberStart ?? 0, true, true),
            ArtworkItemKind.Season => SeasonLabel,
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

        private string SeasonLabel
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(SeasonName))
                {
                    return SeasonName.ToUpperInvariant();
                }

                return SeasonNumber.HasValue
                    ? string.Format(CultureInfo.InvariantCulture, "SEASON {0}", SeasonNumber.Value)
                    : string.Empty;
            }
        }

        /// <summary>
        /// Gets a value indicating whether a cutout style punches out the title itself. A series has
        /// no code or number, so its name becomes the cutout and is not drawn a second time.
        /// </summary>
        public bool CutoutIsTitle => Kind == ArtworkItemKind.Series;

        /// <summary>
        /// Returns the text a cutout style punches out: the <see cref="Code"/>, the featured number
        /// spelled out as a word, or a series' name.
        /// </summary>
        public string CutoutText(CutoutType type)
        {
            if (CutoutIsTitle)
            {
                return (Title ?? string.Empty).ToUpperInvariant();
            }

            if (type == CutoutType.Text && Number.HasValue)
            {
                return EpisodeCodeUtils.FormatEpisodeText(CutoutType.Text, 0, Number.Value);
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
