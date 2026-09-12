using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// The settings document is written by hand, so a rename or a new setting drifts away from it
/// silently. These read the models and the style list and hold the document to them: a style the
/// document never mentions, or a setting it never names, is a gap a reader falls into.
/// </summary>
public class DocumentationTests
{
    private static string Doc(string name)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "docs", name);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException($"Could not locate docs/{name} above the test assembly.");
    }

    private static IEnumerable<string> DisplayLabels(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.GetCustomAttribute<DisplayAttribute>()?.Name)
            .Where(name => !string.IsNullOrEmpty(name))!;
    }

    [Fact]
    public void SettingsDocumentNamesEveryPosterStyle()
    {
        var doc = Doc("SETTINGS.md");

        foreach (var style in Enum.GetNames<PosterStyle>())
        {
            // FrostedGlass reads as "Frosted Glass" in prose, as it does on the page.
            var spaced = string.Concat(style.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));

            Assert.True(
                doc.Contains(style, StringComparison.Ordinal) || doc.Contains(spaced, StringComparison.Ordinal),
                $"docs/SETTINGS.md never mentions the {style} style.");
        }
    }

    [Fact]
    public void SettingsDocumentNamesEverySettingThePagesShow()
    {
        var doc = Doc("SETTINGS.md");
        var labels = DisplayLabels(typeof(PosterSettings)).Concat(DisplayLabels(typeof(LogoSettings)));

        foreach (var label in labels.Distinct())
        {
            Assert.True(
                doc.Contains(label, StringComparison.OrdinalIgnoreCase),
                $"docs/SETTINGS.md never names the \"{label}\" setting.");
        }
    }

    /// <summary>
    /// Settings that have been removed must leave the document with them, or it goes on describing
    /// a control nobody can find.
    /// </summary>
    [Fact]
    public void DocumentsDoNotDescribeSettingsThatNoLongerExist()
    {
        var settings = Doc("SETTINGS.md");
        var readme = Doc("../README.md");

        foreach (var gone in new[] { "Enable Artwork Generation", "EnableProvider" })
        {
            Assert.DoesNotContain(gone, settings, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(gone, readme, StringComparison.OrdinalIgnoreCase);
        }
    }
}
