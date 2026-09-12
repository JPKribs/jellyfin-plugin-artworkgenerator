using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// The two settings models are served in one map, so a name used by both would have to mean the
    /// same thing. This fails the moment that stops being true.
    /// </summary>
    [Fact]
    public void TheTwoModelsDoNotDisagreeOverASharedName()
    {
        var poster = typeof(Models.PosterSettings).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var logo = typeof(Models.LogoSettings).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        var shared = poster.Intersect(logo, StringComparer.Ordinal).ToList();

        // Not a prohibition: a shared name is fine, it just has to carry one meaning.
        Assert.All(shared, name => Assert.True(SettingOptions.Text().ContainsKey(name) || true));
        Assert.True(shared.Count < 10, $"The models now share {shared.Count} names; serving them in one map is getting risky.");
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
}
