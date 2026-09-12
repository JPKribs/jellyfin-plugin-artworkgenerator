using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services.Posters;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Most designs wash the overlay across the frame, so the gradient direction and its far color mean
/// what they say. Three shape the overlay themselves, and for those the same settings either mean
/// something else or nothing at all. These hold that mapping.
/// </summary>
public class OverlaySettingTests
{
    private static IPosterGenerator Generator(PosterStyle style) =>
        PreviewService.GetStyleCatalog().Single(g => g.Style == style);

    private static PosterSettingState State(PosterStyle style, string setting) =>
        Generator(style).SettingRules.TryGetValue(setting, out var s) ? s : PosterSettingState.Optional;

    /// <summary>
    /// A design that builds its own overlay has nothing for a wash direction to act on, so offering
    /// one would be a control that changes nothing.
    /// </summary>
    [Theory]
    [InlineData(PosterStyle.Bloom)]
    [InlineData(PosterStyle.Striped)]
    [InlineData(PosterStyle.Fade)]
    public void DesignsThatShapeTheirOwnOverlayDoNotOfferAGradient(PosterStyle style)
    {
        Assert.Equal(PosterSettingState.Hidden, State(style, PosterSettingRules.OverlayGradient));
    }

    [Theory]
    [InlineData(PosterStyle.Standard)]
    [InlineData(PosterStyle.Cutout)]
    [InlineData(PosterStyle.Brush)]
    [InlineData(PosterStyle.Split)]
    [InlineData(PosterStyle.Frame)]
    public void EveryOtherDesignStillOffersAGradient(PosterStyle style)
    {
        Assert.Equal(PosterSettingState.Optional, State(style, PosterSettingRules.OverlayGradient));
    }

    /// <summary>
    /// Bloom and Striped draw both colors directly, so the second one is theirs to set whatever the
    /// gradient says. Fade draws from one color alone, so a second would do nothing.
    /// </summary>
    [Fact]
    public void TheSecondColorIsOfferedOnlyWhereItIsDrawn()
    {
        Assert.Equal(PosterSettingState.Optional, State(PosterStyle.Bloom, PosterSettingRules.OverlaySecondaryColor));
        Assert.Equal(PosterSettingState.Optional, State(PosterStyle.Striped, PosterSettingRules.OverlaySecondaryColor));
        Assert.Equal(PosterSettingState.Hidden, State(PosterStyle.Fade, PosterSettingRules.OverlaySecondaryColor));
    }

    /// <summary>
    /// "Overlay Color" says nothing useful on a design where it is the bloom, the sash, or the fade,
    /// so each of those names it for what it actually draws.
    /// </summary>
    [Theory]
    [InlineData(PosterStyle.Bloom, "Bloom Color")]
    [InlineData(PosterStyle.Striped, "Band Color")]
    [InlineData(PosterStyle.Fade, "Fade Color")]
    public void ADesignNamesTheOverlayForWhatItDraws(PosterStyle style, string label)
    {
        var text = Generator(style).SettingText;

        Assert.True(text.ContainsKey(PosterSettingRules.OverlayColor));
        Assert.Equal(label, text[PosterSettingRules.OverlayColor].Label);
    }

    /// <summary>
    /// Wording a setting the design hides would put a label on a control nobody can reach.
    /// </summary>
    [Fact]
    public void NoDesignWordsASettingItHides()
    {
        foreach (var generator in PreviewService.GetStyleCatalog())
        {
            foreach (var (setting, _) in generator.SettingText)
            {
                var hidden = generator.SettingRules.TryGetValue(setting, out var state)
                    && state == PosterSettingState.Hidden;

                Assert.False(hidden, $"{generator.Style} words {setting}, which it hides.");
            }
        }
    }
}
