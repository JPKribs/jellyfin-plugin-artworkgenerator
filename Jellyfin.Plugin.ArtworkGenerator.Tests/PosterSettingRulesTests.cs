using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services.Posters;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Tests for the per-style setting rules the configuration page builds its form from.
/// </summary>
public class PosterSettingRulesTests
{
    private static IPosterGenerator Generator(PosterStyle style) =>
        PreviewService.GetStyleCatalog().Single(g => g.Style == style);

    /// <summary>
    /// The page matches these names against the controls' setting names, so a typo would silently
    /// stop hiding a setting.
    /// </summary>
    [Fact]
    public void EveryRuleNamesARealSetting()
    {
        var settings = typeof(PosterSettings).GetProperties().Select(p => p.Name).ToHashSet();

        foreach (var generator in PreviewService.GetStyleCatalog())
        {
            Assert.All(generator.SettingRules.Keys, key => Assert.Contains(key, settings));
        }
    }

    /// <summary>
    /// A style that does not place the series logo, cut out its text, or outline a shape should not
    /// offer those settings.
    /// </summary>
    [Fact]
    public void AStyleWithoutItsOwnSettingsHidesTheStyleSpecificOnes()
    {
        var standard = Generator(PosterStyle.Standard).SettingRules;

        Assert.Equal(PosterSettingState.Hidden, standard[PosterSettingRules.CutoutType]);
        Assert.Equal(PosterSettingState.Hidden, standard[PosterSettingRules.CutoutBorder]);
        Assert.Equal(PosterSettingState.Hidden, standard[PosterSettingRules.LogoHeight]);
    }

    [Theory]
    [InlineData(PosterStyle.Cutout, PosterSettingRules.ShowSecondary)]
    [InlineData(PosterStyle.Numeral, PosterSettingRules.ShowSecondary)]
    [InlineData(PosterStyle.Timeline, PosterSettingRules.ShowSecondary)]
    [InlineData(PosterStyle.Brush, PosterSettingRules.ShowPrimary)]
    [InlineData(PosterStyle.Frame, PosterSettingRules.ShowPrimary)]
    public void AStyleBuiltOnAnElementRequiresIt(PosterStyle style, string setting)
    {
        Assert.Equal(PosterSettingState.Required, Generator(style).SettingRules[setting]);
    }

    [Theory]
    [InlineData(PosterStyle.Standard, PosterSettingRules.ShowPrimary)]
    [InlineData(PosterStyle.Standard, PosterSettingRules.ShowSecondary)]
    [InlineData(PosterStyle.Logo, PosterSettingRules.LogoHeight)]
    [InlineData(PosterStyle.Cutout, PosterSettingRules.CutoutType)]
    [InlineData(PosterStyle.Brush, PosterSettingRules.CutoutBorder)]
    public void AStyleOffersTheSettingsItUses(PosterStyle style, string setting)
    {
        var rules = Generator(style).SettingRules;
        var state = rules.TryGetValue(setting, out var declared) ? declared : PosterSettingState.Optional;

        Assert.Equal(PosterSettingState.Optional, state);
    }

    /// <summary>
    /// Cutout sizes and colors its letters from the cutout itself, so the text settings would lie.
    /// </summary>
    [Fact]
    public void CutoutHidesTheTextSizeAndColor()
    {
        var rules = Generator(PosterStyle.Cutout).SettingRules;

        Assert.Equal(PosterSettingState.Hidden, rules[PosterSettingRules.SecondaryFontSize]);
        Assert.Equal(PosterSettingState.Hidden, rules[PosterSettingRules.SecondaryFontColor]);
    }

    /// <summary>
    /// Only the style that draws a border decides which edge holds the title.
    /// </summary>
    [Fact]
    public void OnlyFrameOffersTheLoneLineSetting()
    {
        Assert.Equal(PosterSettingState.Optional, Generator(PosterStyle.Frame).SettingRules[PosterSettingRules.LoneLineFollowsTitle]);
        Assert.Equal(PosterSettingState.Hidden, Generator(PosterStyle.Standard).SettingRules[PosterSettingRules.LoneLineFollowsTitle]);
        Assert.Equal(PosterSettingState.Hidden, Generator(PosterStyle.Cutout).SettingRules[PosterSettingRules.LoneLineFollowsTitle]);
    }

    /// <summary>
    /// A design may word a shared setting in its own terms, and only for a setting it actually
    /// offers — wording a hidden setting would put labels on a control nobody can see.
    /// </summary>
    [Fact]
    public void ADesignOnlyWordsSettingsItOffers()
    {
        foreach (var generator in PreviewService.GetStyleCatalog())
        {
            foreach (var (setting, text) in generator.SettingText)
            {
                Assert.False(string.IsNullOrWhiteSpace(text.Label));
                Assert.False(string.IsNullOrWhiteSpace(text.Description));

                var offered = !generator.SettingRules.TryGetValue(setting, out var state)
                    || state != PosterSettingState.Hidden;

                Assert.True(offered, $"{generator.Style} words {setting}, which it hides.");
            }
        }
    }

    /// <summary>
    /// The framed design is the one that needed this: its two lines take opposite edges, so the
    /// shared wording would describe something it does not do.
    /// </summary>
    [Fact]
    public void TheFramedDesignWordsItsPlacementControls()
    {
        var frame = Generator(PosterStyle.Frame);

        Assert.True(frame.SettingText.ContainsKey(PosterSettingRules.TextPosition));
        Assert.True(frame.SettingText.ContainsKey(PosterSettingRules.LoneLineFollowsTitle));
        Assert.DoesNotContain("subtitle sit", frame.SettingText[PosterSettingRules.TextPosition].Description, System.StringComparison.OrdinalIgnoreCase);
    }
}
