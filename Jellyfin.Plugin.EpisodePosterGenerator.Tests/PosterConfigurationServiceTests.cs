using System;
using System.Linq;
using Jellyfin.Plugin.EpisodePosterGenerator.Configuration;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Tests;

/// <summary>
/// Tests for bringing pre-profile configurations up to the profile model, and for the lookups.
/// </summary>
public class PosterConfigurationServiceTests
{
    private static PosterConfigurationService Service() => new(NullLogger<PosterConfigurationService>.Instance);

    private static PluginConfiguration LegacyConfig(Guid seriesId, out PosterConfiguration custom)
    {
        var config = new PluginConfiguration();
        config.PosterConfigurations.Add(new PosterConfiguration { Name = "Default", IsDefault = true });

        custom = new PosterConfiguration
        {
            Name = "Anime",
            Settings = new PosterSettings { PosterStyle = PosterStyle.Fade, GenerateBackdrop = true }
        };
        custom.SeriesIds.Add(seriesId);
        config.PosterConfigurations.Add(custom);

        return config;
    }

    /// <summary>
    /// Every design that had series assigned becomes a profile carrying those series, so no series
    /// changes look when the configuration is upgraded.
    /// </summary>
    [Fact]
    public void Initialize_BuildsProfilesFromLegacyDesigns()
    {
        var seriesId = Guid.NewGuid();
        var config = LegacyConfig(seriesId, out var custom);
        var service = Service();

        service.Initialize(config);

        Assert.Equal(2, config.Profiles.Count);
        var defaultProfile = Assert.Single(config.Profiles, p => p.IsDefault);
        var migrated = Assert.Single(config.Profiles, p => !p.IsDefault);

        Assert.Equal(custom.Id, migrated.Id);
        Assert.Equal("Anime", migrated.Name);
        Assert.Equal(new[] { seriesId }, migrated.SeriesIds);
        Assert.Empty(custom.SeriesIds);

        Assert.Same(migrated, service.GetProfileForSeries(seriesId));
        Assert.Same(defaultProfile, service.GetProfileForSeries(Guid.NewGuid()));
    }

    [Fact]
    public void Initialize_KeepsEachSeriesEpisodeDesignAndBackdropChoice()
    {
        var config = LegacyConfig(Guid.NewGuid(), out var custom);
        Service().Initialize(config);

        var defaultDesign = config.PosterConfigurations.Single(c => c.IsDefault);
        var defaultProfile = config.Profiles.Single(p => p.IsDefault);
        var migrated = config.Profiles.Single(p => !p.IsDefault);

        Assert.Equal(defaultDesign.Id, defaultProfile.GetSlot(ArtworkItemKind.Episode, ArtworkSlot.Primary)!.DesignId);
        Assert.Equal(custom.Id, migrated.GetSlot(ArtworkItemKind.Episode, ArtworkSlot.Primary)!.DesignId);

        Assert.False(defaultProfile.GetSlot(ArtworkItemKind.Episode, ArtworkSlot.Backdrop)!.Enabled);
        Assert.True(migrated.GetSlot(ArtworkItemKind.Episode, ArtworkSlot.Backdrop)!.Enabled);
    }

    [Fact]
    public void Initialize_AddsPortraitAndLogoDesignsForTheNewSlots()
    {
        var config = LegacyConfig(Guid.NewGuid(), out _);
        Service().Initialize(config);

        var portrait = Assert.Single(config.PosterConfigurations, c => c.Settings.Shape == ArtworkShape.Portrait);
        Assert.Equal(PosterConfigurationService.DefaultPortraitDesignId, portrait.Id);
        Assert.Equal("2:3", portrait.Settings.PosterDimensionRatio);

        var logo = Assert.Single(config.LogoConfigurations);
        var profile = config.Profiles.Single(p => p.IsDefault);

        Assert.Equal(portrait.Id, profile.GetSlot(ArtworkItemKind.Series, ArtworkSlot.Primary)!.DesignId);
        Assert.Equal(portrait.Id, profile.GetSlot(ArtworkItemKind.Season, ArtworkSlot.Primary)!.DesignId);
        Assert.Equal(logo.Id, profile.GetSlot(ArtworkItemKind.Series, ArtworkSlot.Logo)!.DesignId);
    }

    /// <summary>
    /// Initialize runs on every start and every save until the user saves, so it must not keep
    /// adding designs or profiles.
    /// </summary>
    [Fact]
    public void Initialize_IsIdempotent()
    {
        var config = LegacyConfig(Guid.NewGuid(), out _);
        var service = Service();

        service.Initialize(config);
        service.Initialize(config);

        Assert.Equal(3, config.PosterConfigurations.Count);
        Assert.Single(config.LogoConfigurations);
        Assert.Equal(2, config.Profiles.Count);
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
    public void GetDesignForSlot_FallsBackToTheDefaultForItsShape()
    {
        var config = LegacyConfig(Guid.NewGuid(), out _);
        var service = Service();
        service.Initialize(config);

        var dangling = new SlotAssignment { DesignId = Guid.NewGuid() };

        Assert.Equal(ArtworkShape.Portrait, service.GetDesignForSlot(dangling, ArtworkShape.Portrait).Shape);
        Assert.Same(config.PosterConfigurations.Single(c => c.IsDefault).Settings, service.GetDesignForSlot(dangling, ArtworkShape.Landscape));
    }
}
