using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Xunit;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Tests;

/// <summary>
/// Checks the shipped example templates against the settings model. They are the plugin's worked
/// examples and the source of every demo image, so a template naming a setting that no longer
/// exists would silently render with a default rather than failing, and a style or text edge with
/// no template would go unseen in the demos.
/// </summary>
public class TemplateCoverageTests
{
    /// <summary>The setting names used before the primary/secondary rename. No template may use them.</summary>
    private static readonly string[] LegacyNames =
    [
        "ShowTitle", "ShowEpisode", "TitleUseCustomFont", "EpisodeUseCustomFont",
        "TitleFontFamily", "TitleFontPath", "TitleFontStyle", "TitleFontSize", "TitleFontColor",
        "EpisodeFontFamily", "EpisodeFontPath", "EpisodeFontStyle", "EpisodeFontSize", "EpisodeFontColor",
        "TitleEdge", "LongTitleHandling"
    ];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    // ExamplesDirectory
    // Walks up from the test assembly to the repository's docs/examples folder.
    private static string ExamplesDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "docs", "examples");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException("Could not locate docs/examples above the test assembly.");
    }

    private static IEnumerable<(string Name, JsonElement Settings)> Templates()
    {
        foreach (var file in Directory.GetFiles(ExamplesDirectory(), "Template.json", SearchOption.AllDirectories))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            yield return (Path.GetFileName(Path.GetDirectoryName(file))!, doc.RootElement.GetProperty("settings").Clone());
        }
    }

    /// <summary>Every template deserializes into the settings model the renderer is given.</summary>
    [Fact]
    public void EveryTemplateDeserializes()
    {
        var count = 0;
        foreach (var (name, settings) in Templates())
        {
            var parsed = settings.Deserialize<PosterSettings>(Options);
            Assert.True(parsed != null, $"{name} did not deserialize into the settings model.");
            count++;
        }

        Assert.True(count > 0, "No templates were found to check.");
    }

    /// <summary>
    /// Every key a template sets names a real setting. A typo or a setting renamed without its
    /// templates would otherwise pass silently and render with the default.
    /// </summary>
    [Fact]
    public void EveryTemplateKeyNamesARealSetting()
    {
        var known = typeof(PosterSettings)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, settings) in Templates())
        {
            foreach (var property in settings.EnumerateObject())
            {
                Assert.True(known.Contains(property.Name), $"{name} sets unknown setting '{property.Name}'.");
            }
        }
    }

    /// <summary>No template still uses a name from before the primary/secondary rename.</summary>
    [Fact]
    public void NoTemplateUsesLegacyNames()
    {
        foreach (var (name, settings) in Templates())
        {
            foreach (var legacy in LegacyNames)
            {
                Assert.False(settings.TryGetProperty(legacy, out _), $"{name} still uses the legacy setting '{legacy}'.");
            }
        }
    }

    /// <summary>Every poster style has at least one example, so no style goes unseen in the demos.</summary>
    [Fact]
    public void EveryStyleHasATemplate()
    {
        var covered = Templates()
            .Select(t => t.Settings.GetProperty("PosterStyle").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var style in Enum.GetNames<PosterStyle>())
        {
            Assert.True(covered.Contains(style), $"No example template uses the {style} style.");
        }
    }

    /// <summary>
    /// Every text edge choice has an example. The edges decide which line fills which side of a
    /// framed poster, and only a rendered example shows that the pinned and fill-first choices
    /// actually differ.
    /// </summary>
    [Fact]
    public void EveryTextEdgeHasATemplate()
    {
        var covered = Templates()
            .Where(t => string.Equals(t.Settings.GetProperty("PosterStyle").GetString(), nameof(PosterStyle.Frame), StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Settings.GetProperty("TextEdge").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var edge in Enum.GetNames<TextEdge>())
        {
            Assert.True(covered.Contains(edge), $"No framed example template uses the {edge} text edge.");
        }
    }
}
