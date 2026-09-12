using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using SkiaSharp;

namespace Jellyfin.Plugin.ArtworkGenerator.Utilities;

public static class TextUtils
{
    // Divider between title segments: a spaced dash of any kind, or a colon.
    private static readonly Regex SegmentSeparator = new Regex(@"(\s+[-–—]\s+|:\s*)", RegexOptions.Compiled);

    // TrySplitAtSeparator
    // Splits a name at its first colon or spaced dash into the title and the subtitle, as in
    // "Star Wars: Andor". False when there is no separator or either side would be empty.
    public static bool TrySplitAtSeparator(string text, out string title, out string subtitle)
    {
        title = string.Empty;
        subtitle = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = SegmentSeparator.Match(text);
        if (!match.Success)
        {
            return false;
        }

        title = text[..match.Index].Trim();
        subtitle = text[(match.Index + match.Length)..].Trim();
        return title.Length > 0 && subtitle.Length > 0;
    }

    private const string UnicodeEllipsis = "…";
    private const string AsciiEllipsis = "...";

    // A word-boundary cut is only taken when it keeps at least this share of the available
    // width; below that the cut would leave a stub that reads worse than a mid-word trim.
    private const float MinimumWordCutShare = 0.5f;

    // FitTextLines
    // Applies the configured long title handling and returns the lines to draw.
    // Returns an empty list when the handling drops a title that does not fit.
    public static IReadOnlyList<string> FitTextLines(string title, SKFont font, float maxWidth, LongTextHandling handling)
    {
        ArgumentNullException.ThrowIfNull(font);

        if (string.IsNullOrWhiteSpace(title))
            return Array.Empty<string>();

        if (handling == LongTextHandling.Ellipsis)
            return FitTextToWidth(title, font, maxWidth);

        // Abbreviate and DropName only engage when the title would otherwise be cut:
        // a title that fits on one line, or wraps to two whole lines, renders as normal.
        if (TryFitWhole(title, font, maxWidth, out var lines))
            return lines;

        if (handling == LongTextHandling.DropName)
            return Array.Empty<string>();

        foreach (var candidate in ShorterCandidates(title))
        {
            if (TryFitWhole(candidate, font, maxWidth, out var candidateLines))
                return candidateLines;
        }

        var abbreviation = FitAbbreviation(AbbreviateTitle(title), font, maxWidth);
        return abbreviation != null ? new[] { abbreviation } : Array.Empty<string>();
    }

    // FitTextLines
    // Height-aware variant: fits the title to the width, then checks the resulting block against
    // the vertical space it has been given and re-fits if it would overflow.
    //
    // A run of n lines occupies one line box (ascent plus descent) plus (n-1) line heights, which
    // is how the styles draw them; measuring that way rather than n * lineHeight is what lets the
    // last line that genuinely fits be kept.
    public static IReadOnlyList<string> FitTextLines(
        string title,
        SKFont font,
        float maxWidth,
        float maxHeight,
        float lineHeight,
        LongTextHandling handling)
    {
        ArgumentNullException.ThrowIfNull(font);

        var lines = FitTextLines(title, font, maxWidth, handling);
        if (lines.Count == 0 || lineHeight <= 0f || maxHeight <= 0f)
        {
            return lines;
        }

        var metrics = font.Metrics;
        var lineBox = -metrics.Ascent + metrics.Descent;
        var slack = lineHeight * 0.01f;
        var maxLines = maxHeight + slack < lineBox
            ? 1
            : 1 + (int)Math.Floor((maxHeight - lineBox + slack) / lineHeight);

        if (lines.Count <= maxLines)
        {
            return lines;
        }

        // Too tall. Ellipsis keeps as many lines as fit and trims whatever is left into the
        // last one; the other modes are asking for a shorter title, so re-run them against
        // the width a single line really has.
        if (handling == LongTextHandling.Ellipsis)
        {
            var kept = lines.Take(maxLines - 1).ToList();
            var rest = string.Join(" ", lines.Skip(maxLines - 1));
            kept.Add(TruncateWithEllipsis(rest, font, maxWidth));
            return kept;
        }

        var single = FitTitleLine(title, font, maxWidth * maxLines, handling);
        if (single == null)
        {
            return Array.Empty<string>();
        }

        var refit = FitTextLines(single, font, maxWidth, handling);
        return refit.Count <= maxLines ? refit : refit.Take(maxLines).ToList();
    }

    // FitTitleLine
    // Single line variant of FitTextLines for styles that cannot wrap.
    // Returns null when the handling drops a title that does not fit.
    public static string? FitTitleLine(string title, SKFont font, float maxWidth, LongTextHandling handling)
    {
        ArgumentNullException.ThrowIfNull(font);

        if (string.IsNullOrWhiteSpace(title))
            return null;

        if (font.MeasureText(title) <= maxWidth)
            return title;

        if (handling == LongTextHandling.DropName)
            return null;

        if (handling == LongTextHandling.Abbreviate)
        {
            foreach (var candidate in ShorterCandidates(title))
            {
                if (font.MeasureText(candidate) <= maxWidth)
                    return candidate;
            }

            return FitAbbreviation(AbbreviateTitle(title), font, maxWidth);
        }

        return TruncateWithEllipsis(title, font, maxWidth);
    }

    // TryFitWhole
    // Returns true when the text fits untouched, either on one line or split
    // across two whole lines, and outputs those lines.
    private static bool TryFitWhole(string text, SKFont font, float maxWidth, out IReadOnlyList<string> lines)
    {
        if (font.MeasureText(text) <= maxWidth)
        {
            lines = new[] { text };
            return true;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 1)
        {
            int split = FindBalancedSplitPoint(words, font, maxWidth);
            if (split > 0)
            {
                lines = new[] { string.Join(" ", words[..split]), string.Join(" ", words[split..]) };
                return true;
            }
        }

        lines = Array.Empty<string>();
        return false;
    }

    // ShorterCandidates
    // Natural shorter forms tried before abbreviating: the text before a
    // divider, then the first sentence.
    private static IEnumerable<string> ShorterCandidates(string title)
    {
        var left = LeftOfSeparator(title);
        if (left != null)
            yield return left;

        var sentence = FirstSentence(title);
        if (sentence != null)
            yield return sentence;
    }

    // LeftOfSeparator
    // Returns the text before the first divider (a spaced dash or a colon),
    // or null when the title has no divider. Hyphenated words do not count.
    public static string? LeftOfSeparator(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        var match = SegmentSeparator.Match(title);
        if (!match.Success || match.Index == 0)
            return null;

        var left = title[..match.Index].Trim();
        return left.Length > 0 ? left : null;
    }

    // FirstSentence
    // Returns the first sentence including its punctuation, or null when the
    // title is a single sentence. Very short fragments such as "Mr." are not
    // treated as sentences.
    public static string? FirstSentence(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        for (int i = 0; i < title.Length - 1; i++)
        {
            var c = title[i];
            if ((c == '!' || c == '?' || c == '.') && char.IsWhiteSpace(title[i + 1]))
            {
                var sentence = title[..(i + 1)].Trim();
                if (sentence.Length > 3 && sentence.Length < title.Trim().Length)
                    return sentence;
            }
        }

        return null;
    }

    // AbbreviateTitle
    // Reduces a title to the first letter of every word with a period after
    // each, so "Lord of the Ring" becomes "L.O.T.R.". Dividers are kept
    // between segments, so "The Power that Burns Fire - Akainu's Final Move"
    // becomes "T.P.T.B.F. - A.F.M.". Every word contributes regardless of
    // case, so all lowercase titles abbreviate too.
    public static string AbbreviateTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        var parts = SegmentSeparator.Split(title);
        var pieces = new List<string>();
        string? pendingSeparator = null;

        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
                continue;

            var trimmed = part.Trim();
            if (trimmed == "-" || trimmed == "–" || trimmed == "—")
            {
                pendingSeparator = " - ";
                continue;
            }

            if (trimmed == ":")
            {
                pendingSeparator = ": ";
                continue;
            }

            var abbreviated = AbbreviateWords(part);
            if (abbreviated.Length == 0)
                continue;

            if (pieces.Count > 0 && pendingSeparator != null)
                pieces.Add(pendingSeparator);

            pendingSeparator = null;
            pieces.Add(abbreviated);
        }

        return string.Concat(pieces);
    }

    // AbbreviateWords
    // The first letter of every word, uppercased, each followed by a period.
    private static string AbbreviateWords(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder(words.Length * 2);

        foreach (var word in words)
        {
            var letter = word.FirstOrDefault(char.IsLetterOrDigit);
            if (letter == default(char))
                continue;

            sb.Append(char.ToUpperInvariant(letter));
            sb.Append('.');
        }

        return sb.ToString();
    }

    // FitAbbreviation
    // Shrinks an abbreviation that is still too wide: dividers collapse away
    // and middle initials drop one at a time until it fits, always keeping
    // the first and the last. Returns null when even the shortest form does
    // not fit, so the caller drops the title entirely.
    public static string? FitAbbreviation(string abbreviation, SKFont font, float maxWidth)
    {
        ArgumentNullException.ThrowIfNull(abbreviation);
        ArgumentNullException.ThrowIfNull(font);

        if (font.MeasureText(abbreviation) <= maxWidth)
            return abbreviation;

        var units = new List<string>();
        foreach (var c in abbreviation)
        {
            if (char.IsLetterOrDigit(c))
                units.Add(string.Concat(c, "."));
        }

        while (units.Count > 2 && font.MeasureText(string.Concat(units)) > maxWidth)
        {
            units.RemoveAt(units.Count / 2);
        }

        var reduced = string.Concat(units);
        return reduced.Length > 0 && font.MeasureText(reduced) <= maxWidth ? reduced : null;
    }

    // SplitBalanced
    // Splits text at the word boundary that makes the wider of the two lines as narrow as
    // possible. Returns the text and an empty second line when it is a single word.
    public static (string First, string Second) SplitBalanced(string text, SKFont font)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2)
            return (text.Trim(), string.Empty);

        int best = 1;
        float bestWidest = float.MaxValue;
        for (int i = 1; i < words.Length; i++)
        {
            float widest = Math.Max(
                font.MeasureText(string.Join(" ", words[..i])),
                font.MeasureText(string.Join(" ", words[i..])));

            if (widest < bestWidest)
            {
                bestWidest = widest;
                best = i;
            }
        }

        return (string.Join(" ", words[..best]), string.Join(" ", words[best..]));
    }

    // FitTextToWidth
    // Wraps text to at most two lines, trimming with an ellipsis when it still does not fit.
    //
    // A balanced split is used when both halves fit, because it reads best. When they cannot,
    // the first line is packed with as many whole words as fit and only the second line is
    // trimmed. Splitting evenly and then trimming both halves produced posters reading
    // "The One Where Ev… / Out What Happen…", with a cut in the middle of the thought.
    public static IReadOnlyList<string> FitTextToWidth(string text, SKFont font, float maxWidth)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);

        if (TryFitWhole(text, font, maxWidth, out var whole))
            return whole;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1)
            return new[] { TruncateWithEllipsis(text, font, maxWidth) };

        int count = GreedyLineWordCount(words, font, maxWidth);
        var line1 = string.Join(" ", words[..count]);
        var line2 = string.Join(" ", words[count..]);

        return new[]
        {
            font.MeasureText(line1) <= maxWidth ? line1 : TruncateWithEllipsis(line1, font, maxWidth),
            TruncateWithEllipsis(line2, font, maxWidth)
        };
    }

    // GreedyLineWordCount
    // The most leading words that fit on one line, always leaving at least one word for the
    // next line and taking at least one even when it alone is too wide.
    private static int GreedyLineWordCount(string[] words, SKFont font, float maxWidth)
    {
        int count = 1;
        for (int i = 2; i < words.Length; i++)
        {
            if (font.MeasureText(string.Join(" ", words[..i])) > maxWidth)
                break;

            count = i;
        }

        return count;
    }

    // FindBalancedSplitPoint
    // The word index that splits the text into the two most even lines that both fit, or
    // zero when no split fits both lines.
    private static int FindBalancedSplitPoint(string[] words, SKFont font, float maxWidth)
    {
        int bestSplit = 0;
        float bestDifference = float.MaxValue;

        for (int i = 1; i < words.Length; i++)
        {
            float firstWidth = font.MeasureText(string.Join(" ", words[..i]));
            if (firstWidth > maxWidth)
                break;

            float secondWidth = font.MeasureText(string.Join(" ", words[i..]));
            if (secondWidth > maxWidth)
                continue;

            float difference = Math.Abs(firstWidth - secondWidth);
            if (difference < bestDifference)
            {
                bestDifference = difference;
                bestSplit = i;
            }
        }

        return bestSplit;
    }

    // TruncateWithEllipsis
    // Trims text to fit the width and appends an ellipsis. The cut lands after a whole word
    // when that keeps a reasonable share of the line ("Everybody…" rather than "Everybody Fi…"),
    // and falls back to a character cut for a single long word.
    public static string TruncateWithEllipsis(string text, SKFont font, float maxWidth)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);

        if (font.MeasureText(text) <= maxWidth)
            return text;

        var ellipsis = Ellipsis(font);
        var available = maxWidth - font.MeasureText(ellipsis);
        if (available <= 0f)
            return ellipsis;

        for (int i = text.Length - 1; i > 0; i--)
        {
            if (!char.IsWhiteSpace(text[i]))
                continue;

            var candidate = text[..i].TrimEnd();
            if (candidate.Length == 0)
                break;

            var width = font.MeasureText(candidate);
            if (width > available)
                continue;

            if (width >= available * MinimumWordCutShare)
                return candidate + ellipsis;

            break;
        }

        for (int i = text.Length - 1; i > 0; i--)
        {
            var substring = text[..i].TrimEnd();
            if (substring.Length > 0 && font.MeasureText(substring) <= available)
                return substring + ellipsis;
        }

        return ellipsis;
    }

    // Ellipsis
    // The single ellipsis glyph when the face has one, otherwise three periods.
    private static string Ellipsis(SKFont font)
    {
        return font.ContainsGlyphs(UnicodeEllipsis) ? UnicodeEllipsis : AsciiEllipsis;
    }
}
