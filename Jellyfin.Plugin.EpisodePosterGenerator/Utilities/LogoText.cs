using System;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Utilities
{
    /// <summary>
    /// Chooses and cleans the text a generated logo shows.
    /// </summary>
    public static class LogoText
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

        // [tvdbid-12345], {imdb-tt1234567}: tags media managers put in folder names.
        private static readonly Regex BracketTags = new(@"\s*(\[[^\]]*\]|\{[^}]*\})", RegexOptions.Compiled, RegexTimeout);

        // (2019), anywhere in the name.
        private static readonly Regex ParenthesizedYear = new(@"\s*\((?:19|20)\d{2}\)", RegexOptions.Compiled, RegexTimeout);

        // A bare trailing year. Only applied to folder names: a title such as "Blade Runner 2049"
        // ends in a number that is part of the name.
        private static readonly Regex TrailingYear = new(@"\s+(?:19|20)\d{2}\s*$", RegexOptions.Compiled, RegexTimeout);

        private static readonly Regex RepeatedWhitespace = new(@"\s{2,}", RegexOptions.Compiled, RegexTimeout);

        // Resolve
        // Picks the configured source text for a subject and cleans it. Falls back to the series
        // name when the chosen source is empty for this series.
        public static string Resolve(ArtworkSubject subject, LogoSettings settings)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var raw = settings.TitleSource switch
            {
                LogoTitleSource.OriginalTitle => subject.OriginalTitle,
                LogoTitleSource.SortTitle => subject.SortTitle,
                LogoTitleSource.FolderName => subject.FolderName,
                _ => subject.SeriesName
            };

            var fromFolder = settings.TitleSource == LogoTitleSource.FolderName;
            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = subject.SeriesName;
                fromFolder = false;
            }

            return Clean(raw, settings, fromFolder);
        }

        // Clean
        // Applies the configured cleanup to a name. Each rule only ever removes text, and if the
        // rules would remove everything the original is kept rather than drawing an empty logo.
        public static string Clean(string? raw, LogoSettings settings, bool isFolderName)
        {
            ArgumentNullException.ThrowIfNull(settings);

            var original = (raw ?? string.Empty).Trim();
            var text = original;

            if (isFolderName)
            {
                text = BracketTags.Replace(text, string.Empty);
            }

            if (settings.StripYear)
            {
                text = ParenthesizedYear.Replace(text, string.Empty);
                if (isFolderName)
                {
                    text = TrailingYear.Replace(text, string.Empty);
                }
            }

            if (settings.CutAtSeparator)
            {
                text = TextUtils.LeftOfSeparator(text) ?? text;
            }

            if (!string.IsNullOrWhiteSpace(settings.CustomRegex))
            {
                try
                {
                    text = Regex.Replace(text, settings.CustomRegex, string.Empty, RegexOptions.None, RegexTimeout);
                }
                catch (ArgumentException)
                {
                    // An invalid pattern is ignored rather than failing the logo.
                }
                catch (RegexMatchTimeoutException)
                {
                    // A pathological pattern is ignored rather than stalling the refresh.
                }
            }

            text = RepeatedWhitespace.Replace(text, " ").Trim();
            if (text.Length == 0)
            {
                text = original;
            }

            return settings.Uppercase ? text.ToUpperInvariant() : text;
        }
    }
}
