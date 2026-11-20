using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.ArtworkGenerator.Services.Posters;
using JPKribs.Jellyfin.Base;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Every word a page shows comes from a key. A key the pages use but no string file has shows as the
/// key itself, so these hold the pages and the server's served text to the strings that exist.
/// </summary>
public class TranslationsTests
{
    private static readonly Regex WebKey = new(@"data-str[a-z-]*=""([^""]+)""|\btr\('([^']+)'|\bformat\('([^']+)'", RegexOptions.Compiled);

    private static string ProjectDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "Jellyfin.Plugin.ArtworkGenerator");
            if (File.Exists(Path.Combine(candidate, "Jellyfin.Plugin.ArtworkGenerator.csproj")))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException("Could not locate the plugin project above the test assembly.");
    }

    private static HashSet<string> KnownKeys()
    {
        var project = ProjectDirectory();
        var local = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(project, "Localization", "en-us.json")))!;
        var declared = Regex.Matches(File.ReadAllText(Path.Combine(project, "Jellyfin.Plugin.ArtworkGenerator.csproj")), @"<JellyfinString Include=""([^""]+)""")
            .Select(m => m.Groups[1].Value);

        return local.Keys.Concat(declared).Concat(BaseTranslations.For(null).Keys.Where(key => key.StartsWith("Base.", StringComparison.Ordinal))).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Every key a page names in its markup or script has a string.</summary>
    [Fact]
    public void EveryKeyThePagesUseExists()
    {
        var known = KnownKeys();
        var missing = new List<string>();
        var found = 0;

        foreach (var file in Directory.GetFiles(Path.Combine(ProjectDirectory(), "Web")).Where(f => f.EndsWith(".html", StringComparison.Ordinal) || f.EndsWith(".js", StringComparison.Ordinal)))
        {
            foreach (Match match in WebKey.Matches(File.ReadAllText(file)))
            {
                var key = match.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value;
                found++;
                if (!known.Contains(key))
                {
                    missing.Add($"{Path.GetFileName(file)}: {key}");
                }
            }
        }

        Assert.True(found > 0, "No keys found in Web, so the pattern no longer matches the pages.");
        Assert.True(missing.Count == 0, "Keys with no string:\n  " + string.Join("\n  ", missing.Distinct()));
    }

    /// <summary>Every label, help text, and choice the server serves to a page is a translated string, never a bare key.</summary>
    [Fact]
    public void EverySettingTextTheServerServesIsTranslated()
    {
        var english = BaseTranslations.For(null).Values.ToHashSet(StringComparer.Ordinal);
        var served = new List<string>();

        foreach (var text in SettingOptions.Text().Values
            .Concat(SettingOptions.BackdropText().Values)
            .Concat(SettingOptions.FrameExtractionText().Values))
        {
            served.Add(text.Label);
            if (!string.IsNullOrEmpty(text.Description))
            {
                served.Add(text.Description);
            }
        }

        served.AddRange(SettingOptions.All().Values.Concat(SettingOptions.FrameExtractionOptions().Values).SelectMany(o => o).Select(o => o.Label));

        foreach (var generator in PreviewService.GetStyleCatalog())
        {
            served.Add(BaseTranslations.Get(generator.Description));
            served.Add(BaseTranslations.Get(generator.PrimaryDescription));
            served.Add(BaseTranslations.Get(generator.SecondaryDescription));
            served.AddRange(generator.SettingText.Values.Select(t => SettingOptions.Translate(t, null)).SelectMany(t => new[] { t.Label, t.Description }));
        }

        var untranslated = served.Where(text => !english.Contains(text)).Distinct().ToList();
        Assert.True(untranslated.Count == 0, "Served without a string: " + string.Join(", ", untranslated));
    }

    /// <summary>A page that asks in another language gets the choices in it.</summary>
    [Fact]
    public void ChoicesComeInTheLanguageAsked()
    {
        var series = Assert.Single(SettingOptions.All("de")["PreviewKind"], o => o.Value == "Series");

        Assert.Equal(BaseTranslations.Get(TranslationKeys.Series, "de"), series.Label);
        Assert.NotEqual(BaseTranslations.Get(TranslationKeys.Series), series.Label);
    }
}
