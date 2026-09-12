using System;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services.Posters;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// The configuration page builds its dropdowns and its new-design defaults from these, rather than
/// keeping its own copy of each enum. A setting that goes missing here silently loses its choices
/// in the UI, so the mapping is pinned.
/// </summary>
public class SettingOptionsTests
{
    /// <summary>
    /// Every enum-valued setting offers its choices. This is the guard that matters: a new enum
    /// setting is covered without anyone remembering to register it.
    /// </summary>
    [Fact]
    public void All_CoversEverySettingBackedByAnEnum()
    {
        var options = SettingOptions.All();

        var enumSettings = typeof(PosterSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() == null)
            .Where(p => Nullable.GetUnderlyingType(p.PropertyType) == null)
            .Where(p => p.PropertyType.IsEnum);

        foreach (var setting in enumSettings)
        {
            Assert.True(options.ContainsKey(setting.Name), $"{setting.Name} offers no choices.");
            Assert.Equal(Enum.GetNames(setting.PropertyType).Length, options[setting.Name].Count);
        }
    }

    /// <summary>Every poster style is offered, including one added after this page was written.</summary>
    [Fact]
    public void All_OffersEveryPosterStyle()
    {
        var styles = SettingOptions.All()["PosterStyle"].Select(o => o.Value).ToList();

        Assert.Equal(Enum.GetNames<PosterStyle>().Length, styles.Count);
        Assert.Contains(nameof(PosterStyle.Bloom), styles);
    }

    /// <summary>
    /// A member whose label reads as prose keeps that wording, and one that does not is split into
    /// words from its own name.
    /// </summary>
    [Theory]
    [InlineData("TextEdge", "TopFirst", "Top edge first")]
    [InlineData("TextEdge", "AlwaysBottom", "Title always bottom")]
    [InlineData("CanvasSource", "Extract", "Extract Frame from Video")]
    [InlineData("OverlayGradient", "LeftToRight", "Left to Right")]
    [InlineData("PosterStyle", "FrostedGlass", "Frosted Glass")]
    [InlineData("PosterStyle", "Standard", "Standard")]
    [InlineData("LongTextHandling", "DropName", "Drop Name")]
    public void All_LabelsComeFromTheEnumItself(string setting, string value, string expected)
    {
        var option = Assert.Single(SettingOptions.All()[setting], o => o.Value == value);
        Assert.Equal(expected, option.Label);
    }

    /// <summary>The font style choices are the ones the font parser actually understands.</summary>
    [Fact]
    public void All_OffersTheFontStylesTheParserAccepts()
    {
        foreach (var setting in new[] { "PrimaryFontStyle", "SecondaryFontStyle" })
        {
            Assert.Equal(FontUtils.FontStyles, SettingOptions.All()[setting].Select(o => o.Value).ToList());
        }
    }

    /// <summary>
    /// The legacy setting names kept only for migration are not offered, so the page cannot show a
    /// dropdown for a setting nothing reads any more.
    /// </summary>
    [Theory]
    [InlineData("TitleEdge")]
    [InlineData("LongTitleHandling")]
    [InlineData("ShowTitle")]
    public void All_AndDefaults_LeaveOutLegacyNames(string legacy)
    {
        Assert.False(SettingOptions.All().ContainsKey(legacy));
        Assert.False(SettingOptions.Defaults().ContainsKey(legacy));
    }

    /// <summary>
    /// A new design starts from exactly what the renderer would apply, so the page cannot drift
    /// from the settings model's own defaults.
    /// </summary>
    [Fact]
    public void Defaults_MatchAFreshSettingsObject()
    {
        var expected = new PosterSettings();
        var defaults = SettingOptions.Defaults();

        Assert.Equal(expected.PosterStyle.ToString(), defaults["PosterStyle"]);
        Assert.Equal(expected.CanvasSource.ToString(), defaults["CanvasSource"]);
        Assert.Equal(expected.TextEdge.ToString(), defaults["TextEdge"]);
        Assert.Equal(expected.PrimaryFontSize, defaults["PrimaryFontSize"]);
        Assert.Equal(expected.ShowPrimary, defaults["ShowPrimary"]);
        Assert.Equal(expected.PosterDimensionRatio, defaults["PosterDimensionRatio"]);
        Assert.Equal(expected.ElementSpacing, defaults["ElementSpacing"]);
    }

    /// <summary>Enum defaults are sent as their names, which is what the dropdowns select by.</summary>
    [Fact]
    public void Defaults_SendEnumsAsNames()
    {
        var defaults = SettingOptions.Defaults();

        Assert.IsType<string>(defaults["PosterStyle"]);
        Assert.Contains(defaults["PosterStyle"], SettingOptions.All()["PosterStyle"].Select(o => (object?)o.Value));
    }
}
