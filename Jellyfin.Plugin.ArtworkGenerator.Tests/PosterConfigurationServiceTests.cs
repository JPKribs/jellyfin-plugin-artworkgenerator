using System;
using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Configuration;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services;
using Jellyfin.Plugin.ArtworkGenerator.Services.Artwork;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Tests for bringing pre-profile configurations up to the profile model, and for the lookups.
/// </summary>
public class PosterConfigurationServiceTests
{
    private static PosterConfigurationService Service() => new(NullLogger<PosterConfigurationService>.Instance);

    private static PluginConfiguration Config()
    {
        var config = new PluginConfiguration();
        config.PosterConfigurations.Add(new PosterConfiguration { Name = "Default", IsDefault = true });
        config.PosterConfigurations.Add(new PosterConfiguration
        {
            Name = "Anime",
            Settings = new PosterSettings { PosterStyle = PosterStyle.Fade, GenerateBackdrop = true }
        });

        return config;
    }

    [Fact]
    public void Initialize_AddsALogoDesignAndPointsEveryPosterSlotAtTheDefaultDesign()
    {
        var config = Config();
        var service = Service();
        service.Initialize(config);

        var design = config.PosterConfigurations.Single(c => c.IsDefault);
        var logo = Assert.Single(service.GetLogoDesigns());
        Assert.Equal("Default", logo.Name);
        var profile = config.Profiles.Single(p => p.IsDefault);

        Assert.Equal(design.Id, profile.GetSlot(ArtworkItemKind.Series, ArtworkSlot.Primary)!.DesignId);
        Assert.Equal(design.Id, profile.GetSlot(ArtworkItemKind.Season, ArtworkSlot.Primary)!.DesignId);
        Assert.Equal(design.Id, profile.GetSlot(ArtworkItemKind.Series, ArtworkSlot.Thumb)!.DesignId);
        Assert.Equal(logo.Id, profile.GetSlot(ArtworkItemKind.Series, ArtworkSlot.Logo)!.DesignId);
    }

    /// <summary>
    /// Initialize runs on every start and every save until the user saves, so it must not keep
    /// adding designs or profiles.
    /// </summary>
    [Fact]
    public void Initialize_IsIdempotent()
    {
        var config = Config();
        var service = Service();

        service.Initialize(config);
        service.Initialize(config);

        Assert.Equal(2, config.PosterConfigurations.Count);
        Assert.Single(service.GetLogoDesigns());
        Assert.Single(config.Profiles);
        Assert.All(config.Profiles, p => Assert.Equal(ArtworkProfile.SupportedSlots.Count, p.Slots.Count));
    }

    /// <summary>
    /// A profile saved by an older version gains the new slots switched off, so upgrading never
    /// starts generating images the user did not ask for.
    /// </summary>
    [Fact]
    public void Initialize_AddsMissingSlotsSwitchedOff()
    {
        var config = new PluginConfiguration();
        config.Profiles.Add(new ArtworkProfile { Name = "Default", IsDefault = true });

        Service().Initialize(config);

        var profile = Assert.Single(config.Profiles);
        Assert.Equal(ArtworkProfile.SupportedSlots.Count, profile.Slots.Count);
        Assert.All(profile.Slots, s => Assert.False(s.Enabled));
    }

    [Fact]
    public void GetDesignForSlot_FallsBackToTheDefaultDesign()
    {
        var config = Config();
        var service = Service();
        service.Initialize(config);

        var dangling = new SlotAssignment { DesignId = Guid.NewGuid() };

        Assert.Same(config.PosterConfigurations.Single(c => c.IsDefault).Settings, service.GetDesignForSlot(dangling));
    }

    [Fact]
    public void SaveLogoDesigns_PersistsAndRefreshesTheLookups()
    {
        var store = new LogoDesignStore();
        var service = new PosterConfigurationService(NullLogger<PosterConfigurationService>.Instance, store);
        var config = new PluginConfiguration();
        service.Initialize(config);

        var added = new LogoConfiguration { Name = "Outlined", Settings = new LogoSettings { OutlineEnabled = true } };
        service.SaveLogoDesigns(config, new[] { service.GetLogoDesigns()[0], added });

        Assert.Equal(2, store.Load().Count);
        Assert.Equal(2, service.GetLogoDesigns().Count);
        Assert.True(service.GetLogoForSlot(new SlotAssignment { DesignId = added.Id }).OutlineEnabled);
        Assert.NotEqual(Guid.Empty, added.Id);
    }

    [Fact]
    public void SaveLogoDesigns_RejectsAnEmptyList()
    {
        var service = Service();
        var config = new PluginConfiguration();
        service.Initialize(config);

        Assert.Throws<ArgumentException>(() => service.SaveLogoDesigns(config, Array.Empty<LogoConfiguration>()));
    }

}
