using System;
using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services.Posters;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Adding a design should be a class and an enum value, with nothing to remember to register. These
/// hold that: the catalog is found by reflection, so a class that exists is offered and an enum
/// value with no class behind it is caught here rather than silently drawing something else.
/// </summary>
public class StyleCatalogTests
{
    [Fact]
    public void EveryStyleHasItsOwnGenerator()
    {
        foreach (var style in Enum.GetValues<PosterStyle>())
        {
            var generator = PreviewService.CreateGenerator(style, NullLoggerFactory.Instance);

            Assert.Equal(style, generator.Style);
        }
    }

    [Fact]
    public void TheCatalogCoversEveryStyleExactlyOnce()
    {
        var catalog = PreviewService.GetStyleCatalog();

        Assert.Equal(Enum.GetValues<PosterStyle>().Length, catalog.Count);
        Assert.Equal(catalog.Count, catalog.Select(g => g.Style).Distinct().Count());
    }

    /// <summary>
    /// The page renders the list the server gives it, so the order is decided here: the plain design
    /// first, because it is the starting point, and the rest alphabetical so a reader can find one.
    /// </summary>
    [Fact]
    public void TheStyleListLeadsWithStandardThenRunsAlphabetically()
    {
        var styles = SettingOptions.All()["PosterStyle"];

        Assert.Equal(nameof(PosterStyle.Standard), styles[0].Value);

        var rest = styles.Skip(1).Select(s => s.Label).ToList();
        Assert.Equal(rest.OrderBy(l => l, StringComparer.OrdinalIgnoreCase), rest);
    }

    /// <summary>
    /// Everything the page shows about a design comes from the design itself, so a new one arrives
    /// described, with its own rules and wording, without the page being touched.
    /// </summary>
    [Fact]
    public void EveryDesignDescribesItself()
    {
        foreach (var generator in PreviewService.GetStyleCatalog())
        {
            Assert.False(string.IsNullOrWhiteSpace(generator.Description));
            Assert.False(string.IsNullOrWhiteSpace(generator.PrimaryDescription));
            Assert.False(string.IsNullOrWhiteSpace(generator.SecondaryDescription));
            Assert.NotNull(generator.SettingRules);
            Assert.NotNull(generator.SettingText);
        }
    }
}
