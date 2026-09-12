using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.ArtworkGenerator.Services.Posters;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// The configuration pages render each setting's label and help text from the settings models, so a
/// setting a page shows but the model has no words for would render blank.
/// </summary>
public class SettingTextTests
{
    private static string ConfigurationDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "Jellyfin.Plugin.ArtworkGenerator", "Configuration");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException("Could not locate the Configuration folder above the test assembly.");
    }

    private static IEnumerable<(string Page, string Setting)> RenderedSettings()
    {
        foreach (var page in Directory.GetFiles(ConfigurationDirectory(), "ag_*.html"))
        {
            var markup = File.ReadAllText(page);
            foreach (Match match in Regex.Matches(markup, @"data-setting=""([A-Za-z]+)"""))
            {
                yield return (Path.GetFileName(page), match.Groups[1].Value);
            }
        }
    }

    /// <summary>Every setting a page renders has a label in the model.</summary>
    [Fact]
    public void EverySettingRenderedByAPageHasALabel()
    {
        var text = SettingOptions.Text();

        foreach (var (page, setting) in RenderedSettings().Distinct())
        {
            Assert.True(text.ContainsKey(setting), $"{page} renders '{setting}', which has no Display label.");
            Assert.False(string.IsNullOrWhiteSpace(text[setting].Label), $"'{setting}' has an empty label.");
        }
    }

    /// <summary>
    /// Options, defaults, and text for both settings models are served in one map keyed by property
    /// name, which only works while the two models share no name. If they ever do, one silently
    /// wins and a page shows the wrong choices; this fails first.
    /// </summary>
    [Fact]
    public void TheTwoSettingsModelsShareNoPropertyName()
    {
        var poster = typeof(Models.PosterSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && Nullable.GetUnderlyingType(p.PropertyType) == null)
            .Select(p => p.Name);

        var logo = typeof(Models.LogoSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .Select(p => p.Name);

        var shared = poster.Intersect(logo, StringComparer.Ordinal).ToList();

        Assert.True(shared.Count == 0, $"The models now share {string.Join(", ", shared)}.");
    }

    /// <summary>A logo design's settings are served too, not just a poster design's.</summary>
    [Fact]
    public void LogoSettingsAreServedAlongsidePosterSettings()
    {
        var options = SettingOptions.All();
        var defaults = SettingOptions.Defaults();

        foreach (var setting in new[] { "TitleSource", "SubtitleMode", "ColorSource", "Fill", "MaxLines", "FontStyle" })
        {
            Assert.True(options.ContainsKey(setting), $"{setting} offers no choices.");
        }

        Assert.Equal(new Models.LogoSettings().Color, defaults["Color"]);
        Assert.Equal(new Models.LogoSettings().Width, defaults["Width"]);
        Assert.Equal(new Models.PosterSettings().PrimaryFontSize, defaults["PrimaryFontSize"]);
    }

    /// <summary>The wording the page used for these choices is kept, not derived from the names.</summary>
    [Theory]
    [InlineData("SubtitleMode", "Keep", "Draw the whole name")]
    [InlineData("SubtitleMode", "SubtitleLarge", "Subtitle large, title small above")]
    [InlineData("ColorSource", "Fixed", "Chosen Color")]
    [InlineData("Fill", "Photo", "A frame from the show")]
    [InlineData("TitleSource", "OriginalTitle", "Original Title")]
    [InlineData("MaxLines", "2", "Up to two lines")]
    public void LogoChoicesKeepTheirWording(string setting, string value, string expected)
    {
        var option = Assert.Single(SettingOptions.All()[setting], o => o.Value == value);
        Assert.Equal(expected, option.Label);
    }

    /// <summary>A description, when present, is a sentence rather than a fragment.</summary>
    [Fact]
    public void DescriptionsReadAsSentences()
    {
        foreach (var (setting, text) in SettingOptions.Text())
        {
            if (string.IsNullOrEmpty(text.Description))
            {
                continue;
            }

            Assert.True(char.IsUpper(text.Description[0]), $"'{setting}' description does not start with a capital.");
            Assert.EndsWith(".", text.Description.TrimEnd(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The profile page's backdrop fields render their wording from the settings model too, so a
    /// backdrop setting with no words would render a blank label and an empty help line.
    /// </summary>
    [Fact]
    public void EveryBackdropSettingThePageShowsHasTextOnTheModel()
    {
        var page = File.ReadAllText(Path.Combine(ConfigurationDirectory(), "ag_profiles.html"));
        var rendered = Regex.Matches(page, "data-backdrop-setting=\"(\\w+)\"")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(rendered);

        var text = SettingOptions.BackdropText();
        foreach (var setting in rendered)
        {
            Assert.True(text.ContainsKey(setting), $"ag_profiles.html shows {setting}, which has no Display text.");
            Assert.False(string.IsNullOrWhiteSpace(text[setting].Label));
            Assert.False(string.IsNullOrWhiteSpace(text[setting].Description));
        }
    }
}
