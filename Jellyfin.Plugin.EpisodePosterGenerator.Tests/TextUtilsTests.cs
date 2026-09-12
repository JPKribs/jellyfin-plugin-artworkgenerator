using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Utilities;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Tests;

/// <summary>
/// Tests for the long title helpers in <see cref="TextUtils"/>.
/// </summary>
public class TextUtilsTests
{
    [Theory]
    [InlineData("Ancient History - The Harley Passed Down by Elbaph", "Ancient History")]
    [InlineData("Spider-Man: No Way Home", "Spider-Man")]
    [InlineData("The Power That Burns Fire - Akainu's Final Move", "The Power That Burns Fire")]
    [InlineData("A Title With No Divider", null)]
    [InlineData("Well-Meaning Hyphenated Words", null)]
    public void LeftOfSeparator_ReturnsTextBeforeTheDivider(string title, string? expected)
    {
        Assert.Equal(expected, TextUtils.LeftOfSeparator(title));
    }

    [Theory]
    [InlineData("I'm Luffy! The Man Who's Gonna Be King of the Pirates!", "I'm Luffy!")]
    [InlineData("Who Are You? I Am Me.", "Who Are You?")]
    [InlineData("A Single Sentence Title", null)]
    [InlineData("Mr. Smith Goes to Washington", null)]
    public void FirstSentence_ReturnsTheFirstSentence(string title, string? expected)
    {
        Assert.Equal(expected, TextUtils.FirstSentence(title));
    }

    [Fact]
    public void FitAbbreviation_DropsMiddleInitialsThenGivesUp()
    {
        using var font = new SKFont(SKTypeface.Default, 20f);

        var full = "T.P.T.B.F. - A.F.M.";
        Assert.Equal(full, TextUtils.FitAbbreviation(full, font, font.MeasureText(full)));

        var reduced = TextUtils.FitAbbreviation(full, font, font.MeasureText("T.M."));
        Assert.Equal("T.M.", reduced);

        Assert.Null(TextUtils.FitAbbreviation(full, font, 1f));
    }

    [Theory]
    [InlineData("Lord of the Ring", "L.O.T.R.")]
    [InlineData("The Power that Burns Fire - Akainu's Final Move", "T.P.T.B.F. - A.F.M.")]
    [InlineData("Ancient History: The Harley", "A.H.: T.H.")]
    [InlineData("all lowercase words", "A.L.W.")]
    [InlineData("some show - all lowercase everywhere", "S.S. - A.L.E.")]
    [InlineData("Hone Your Moving Fastball", "H.Y.M.F.")]
    public void AbbreviateTitle_UsesEveryWordWithPeriodsAndKeepsDividers(string title, string expected)
    {
        Assert.Equal(expected, TextUtils.AbbreviateTitle(title));
    }

    /// <summary>
    /// The width-only fit can return two lines for a slot that only has room for one, which is why
    /// styles used to reserve a fixed two lines regardless. The height-aware overload is what lets
    /// them reserve what is actually drawn.
    /// </summary>
    [Fact]
    public void FitTextLines_RespectsTheHeightItIsGiven()
    {
        using var font = new SKFont(SKTypeface.Default, 20f);
        const string longTitle = "A Fairly Long Episode Title That Wraps";

        var twoLines = TextUtils.FitTextLines(longTitle, font, 150f, LongTextHandling.Ellipsis);
        Assert.True(twoLines.Count > 1, "precondition: this title should wrap at that width");

        // Room for one line only.
        var oneLine = TextUtils.FitTextLines(longTitle, font, 150f, 24f, 24f, LongTextHandling.Ellipsis);
        Assert.Single(oneLine);

        // Room for two.
        var fits = TextUtils.FitTextLines(longTitle, font, 150f, 60f, 24f, LongTextHandling.Ellipsis);
        Assert.Equal(twoLines.Count, fits.Count);
    }

    [Fact]
    public void FitTextLines_HeightAware_NeverExceedsTheAllowedLineCount()
    {
        using var font = new SKFont(SKTypeface.Default, 20f);
        const string longTitle = "An Extremely Long Episode Title That Will Wrap Several Times Over";

        foreach (var handling in new[] { LongTextHandling.Ellipsis, LongTextHandling.Abbreviate, LongTextHandling.DropName })
        {
            var lines = TextUtils.FitTextLines(longTitle, font, 120f, 24f, 24f, handling);
            Assert.True(lines.Count <= 1, $"{handling} returned {lines.Count} lines for a one line slot");
        }
    }

    [Fact]
    public void FitTextLines_HeightAware_IsAPassThroughWhenHeightIsUnconstrained()
    {
        using var font = new SKFont(SKTypeface.Default, 20f);
        const string title = "Short Title";

        var plain = TextUtils.FitTextLines(title, font, 500f, LongTextHandling.Ellipsis);
        var sized = TextUtils.FitTextLines(title, font, 500f, 0f, 0f, LongTextHandling.Ellipsis);

        Assert.Equal(plain, sized);
    }

    /// <summary>
    /// A run of n lines occupies one line box (ascent plus descent) plus (n-1) line heights,
    /// which is exactly how <see cref="TextStyle.BlockHeight"/> reserves it. A zone sized that
    /// way must admit that many lines, and one line height less must admit one fewer.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FitTextLines_CountsLinesTheWayTheStylesDrawThem(int lineCount)
    {
        using var style = PaintFactory.CreateTextStyle(SKColors.White, 20f, SKTypeface.Default, 1080f);
        const string longTitle = "One Two Three Four Five Six Seven Eight Nine Ten Eleven Twelve";

        var zone = style.BlockHeight(lineCount);
        var lines = TextUtils.FitTextLines(longTitle, style.Font, 90f, zone, style.LineHeight, LongTextHandling.Ellipsis);
        Assert.True(lines.Count <= lineCount, $"a {lineCount} line zone produced {lines.Count} lines");

        if (lineCount > 1)
        {
            var smaller = TextUtils.FitTextLines(longTitle, style.Font, 90f, zone - style.LineHeight, style.LineHeight, LongTextHandling.Ellipsis);
            Assert.True(smaller.Count <= lineCount - 1, $"a {lineCount - 1} line zone produced {smaller.Count} lines");
        }
    }

    /// <summary>
    /// When a title cannot fit two whole lines, the first line is packed with whole words and
    /// only the last line is trimmed. Splitting evenly and trimming both halves produced
    /// "The One Where Ev… / Out What Happen…", cut in the middle of the thought.
    /// </summary>
    [Fact]
    public void FitTextToWidth_TrimsOnlyTheLastLine()
    {
        using var font = new SKFont(SKTypeface.Default, 20f);
        const string title = "The One Where Everybody Finds Out What Happened at the Wedding and Then Some";

        var maxWidth = font.MeasureText("The One Where Everybody Finds") + 1f;
        var lines = TextUtils.FitTextToWidth(title, font, maxWidth);

        Assert.Equal(2, lines.Count);
        Assert.Equal("The One Where Everybody Finds", lines[0]);
        Assert.StartsWith("Out What", lines[1], System.StringComparison.Ordinal);
        Assert.True(lines[1].EndsWith('…') || lines[1].EndsWith("...", System.StringComparison.Ordinal));
        Assert.True(font.MeasureText(lines[1]) <= maxWidth);
    }

    /// <summary>
    /// The trim lands after a whole word when that keeps a reasonable share of the line.
    /// </summary>
    [Fact]
    public void TruncateWithEllipsis_PrefersAWordBoundary()
    {
        using var font = new SKFont(SKTypeface.Default, 20f);
        const string text = "Everybody Finds Out What Happened";

        var maxWidth = font.MeasureText("Everybody Finds Out Wha");
        var trimmed = TextUtils.TruncateWithEllipsis(text, font, maxWidth);

        Assert.StartsWith("Everybody Finds Out", trimmed, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Wha", trimmed, System.StringComparison.Ordinal);
        Assert.True(font.MeasureText(trimmed) <= maxWidth);
    }

    [Fact]
    public void TruncateWithEllipsis_CutsASingleLongWordByCharacter()
    {
        using var font = new SKFont(SKTypeface.Default, 20f);
        const string text = "Supercalifragilisticexpialidocious";

        var maxWidth = font.MeasureText("Supercalifrag");
        var trimmed = TextUtils.TruncateWithEllipsis(text, font, maxWidth);

        Assert.StartsWith("Super", trimmed, System.StringComparison.Ordinal);
        Assert.True(trimmed.Length < text.Length);
        Assert.True(font.MeasureText(trimmed) <= maxWidth);
    }
}
