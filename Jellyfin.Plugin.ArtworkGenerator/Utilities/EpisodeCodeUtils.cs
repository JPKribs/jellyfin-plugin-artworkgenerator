using System.Globalization;
using Jellyfin.Plugin.ArtworkGenerator.Models;

namespace Jellyfin.Plugin.ArtworkGenerator.Utilities;

public static class EpisodeCodeUtils
{
    // FormatEpisodeCode
    // Formats season and episode numbers into standard SxxExx notation.
    public static string FormatEpisodeCode(int seasonNumber, int episodeNumber)
    {
        string seasonFormat = GetNumberFormat(seasonNumber);
        string episodeFormat = GetNumberFormat(episodeNumber);

        return $"S{seasonNumber.ToString(seasonFormat, CultureInfo.InvariantCulture)}E{episodeNumber.ToString(episodeFormat, CultureInfo.InvariantCulture)}";
    }

    // FormatSeasonCode
    // Formats a season number as S01, padded the same way as the episode code.
    public static string FormatSeasonCode(int seasonNumber)
    {
        return $"S{seasonNumber.ToString(GetNumberFormat(seasonNumber), CultureInfo.InvariantCulture)}";
    }

    // FormatEpisodeText
    // Formats episode number as text or code based on the cutout type.
    public static string FormatEpisodeText(CutoutType cutoutType, int seasonNumber, int episodeNumber)
    {
        return cutoutType switch
        {
            CutoutType.Text => episodeNumber >= 100
                ? episodeNumber.ToString(CultureInfo.InvariantCulture)
                : NumberUtils.NumberToWords(episodeNumber),

            CutoutType.Code => FormatEpisodeCode(seasonNumber, episodeNumber),

            _ => episodeNumber >= 100
                ? episodeNumber.ToString(CultureInfo.InvariantCulture)
                : NumberUtils.NumberToWords(episodeNumber)
        };
    }

    // FormatFullText
    // Formats a complete season and episode label based on inclusion flags.
    public static string FormatFullText(int seasonNumber, int episodeNumber, bool includeSeason, bool includeEpisode)
    {
        if (includeSeason && includeEpisode)
        {
            return $"SEASON {seasonNumber} • EPISODE {episodeNumber}";
        }

        if (includeSeason)
        {
            return $"SEASON {seasonNumber}";
        }

        if (includeEpisode)
        {
            return $"EPISODE {episodeNumber}";
        }

        return string.Empty;
    }

    // GetNumberFormat
    // Determines the appropriate number format string based on digit count.
    private static string GetNumberFormat(int number)
    {
        return number switch
        {
            >= 1000 => "D4",
            >= 100 => "D3",
            _ => "D2"
        };
    }
}
