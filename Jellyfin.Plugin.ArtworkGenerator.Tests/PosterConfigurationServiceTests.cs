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

        Assert.Same(migrated, service.GetProfileFor(ArtworkItemKind.Series, seriesId));
        Assert.Same(defaultProfile, service.GetProfileFor(ArtworkItemKind.Series, Guid.NewGuid()));
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
    public void Initialize_AddsALogoDesignAndPointsEveryPosterSlotAtTheDefaultDesign()
    {
        var config = LegacyConfig(Guid.NewGuid(), out _);
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
    /// An earlier build created a separate portrait design. Now that every design renders both
    /// shapes it is dropped, and slots that used it move to the default design.
    /// </summary>
    [Fact]
    public void Initialize_RetiresTheSeparatePortraitDesign()
    {
        var config = new PluginConfiguration();
        var design = new PosterConfiguration { Name = "Default", IsDefault = true };
        config.PosterConfigurations.Add(design);
        config.PosterConfigurations.Add(new PosterConfiguration { Id = PosterConfigurationService.DefaultPortraitDesignId, Name = "Default Portrait" });

        var profile = new ArtworkProfile { Name = "Default", IsDefault = true };
        profile.Slots.Add(new SlotAssignment
        {
            Kind = ArtworkItemKind.Series,
            Slot = ArtworkSlot.Primary,
            Enabled = true,
            DesignId = PosterConfigurationService.DefaultPortraitDesignId
        });
        config.Profiles.Add(profile);

        Service().Initialize(config);

        Assert.Single(config.PosterConfigurations);
        Assert.Equal(design.Id, profile.GetSlot(ArtworkItemKind.Series, ArtworkSlot.Primary)!.DesignId);
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

        Assert.Equal(2, config.PosterConfigurations.Count);
        Assert.Single(service.GetLogoDesigns());
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
    public void GetDesignForSlot_FallsBackToTheDefaultDesign()
    {
        var config = LegacyConfig(Guid.NewGuid(), out _);
        var service = Service();
        service.Initialize(config);

        var dangling = new SlotAssignment { DesignId = Guid.NewGuid() };

        Assert.Same(config.PosterConfigurations.Single(c => c.IsDefault).Settings, service.GetDesignForSlot(dangling));
    }

    /// <summary>
    /// Logo designs live in their own file. Any left in an older configuration move across on load,
    /// and the configuration's copy is dropped so the next save cleans it out of the XML.
    /// </summary>
    [Fact]
    public void Initialize_MovesLogoDesignsOutOfTheConfiguration()
    {
        var config = LegacyConfig(Guid.NewGuid(), out _);
        config.LogoConfigurations.Add(new LogoConfiguration { Name = "Mine", Settings = new LogoSettings { LetterCase = LogoCase.Uppercase } });

        var store = new LogoDesignStore();
        var service = new PosterConfigurationService(NullLogger<PosterConfigurationService>.Instance, store);
        service.Initialize(config);

        Assert.Empty(config.LogoConfigurations);
        var moved = Assert.Single(store.Load());
        Assert.Equal("Mine", moved.Name);
        Assert.Equal(LogoCase.Uppercase, moved.Settings.LetterCase);
        Assert.Same(moved.Settings, service.GetLogoForSlot(new SlotAssignment { DesignId = moved.Id }));
    }

    /// <summary>
    /// An earlier build named the synthesized design "Default Logo"; every synthesized default is
    /// simply "Default".
    /// </summary>
    [Fact]
    public void Initialize_RenamesTheOldDefaultLogoName()
    {
        var store = new LogoDesignStore();
        store.Save(new[] { new LogoConfiguration { Id = PosterConfigurationService.DefaultLogoDesignId, Name = "Default Logo" } });

        var service = new PosterConfigurationService(NullLogger<PosterConfigurationService>.Instance, store);
        service.Initialize(new PluginConfiguration());

        Assert.Equal("Default", Assert.Single(store.Load()).Name);
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

    /// <summary>
    /// A graphic used to be sized on each axis, which could stretch it. The larger of the two
    /// becomes the box it is now fitted inside.
    /// </summary>
    [Fact]
    public void Initialize_FoldsAPerAxisGraphicSizeIntoOne()
    {
        var config = new PluginConfiguration();
        var design = new PosterConfiguration { Name = "Default", IsDefault = true };
        design.Settings.GraphicPath = "/graphics/badge.png";
        design.Settings.GraphicWidth = 40f;
        design.Settings.GraphicHeight = 25f;
        config.PosterConfigurations.Add(design);

        Service().Initialize(config);

        Assert.Equal(40f, design.Settings.GraphicSize);
        Assert.Null(design.Settings.GraphicWidth);
        Assert.Null(design.Settings.GraphicHeight);
    }

    /// <summary>
    /// A design saved under the old title/episode setting names keeps its fonts and colors: the
    /// values move onto the primary/secondary names and the old ones are cleared.
    /// </summary>
    [Fact]
    public void Initialize_MigratesLegacyTextSettings()
    {
        var config = new PluginConfiguration();
        var design = new PosterConfiguration { Name = "Default", IsDefault = true };
        design.Settings.ShowTitle = false;
        design.Settings.TitleFontSize = 12f;
        design.Settings.TitleFontFamily = "Georgia";
        design.Settings.EpisodeFontColor = "#FF00FF00";
        design.Settings.TitleEdge = TextEdge.AlwaysBottom;
        design.Settings.LongTitleHandling = LongTextHandling.Abbreviate;
        config.PosterConfigurations.Add(design);

        Service().Initialize(config);

        Assert.False(design.Settings.ShowPrimary);
        Assert.Equal(12f, design.Settings.PrimaryFontSize);
        Assert.Equal("Georgia", design.Settings.PrimaryFontFamily);
        Assert.Equal("#FF00FF00", design.Settings.SecondaryFontColor);
        Assert.Equal(TextEdge.AlwaysBottom, design.Settings.TextEdge);
        Assert.Equal(LongTextHandling.Abbreviate, design.Settings.LongTextHandling);

        Assert.Null(design.Settings.ShowTitle);
        Assert.Null(design.Settings.TitleFontSize);
        Assert.Null(design.Settings.TitleFontFamily);
        Assert.Null(design.Settings.EpisodeFontColor);
        Assert.Null(design.Settings.TitleEdge);
        Assert.Null(design.Settings.LongTitleHandling);
    }

    /// <summary>
    /// A design already saved under the current names is left alone, so migration cannot overwrite
    /// a current setting with a stale one.
    /// </summary>
    [Fact]
    public void Initialize_LeavesCurrentTextSettingsAlone()
    {
        var config = new PluginConfiguration();
        var design = new PosterConfiguration { Name = "Default", IsDefault = true };
        design.Settings.PrimaryFontFamily = "Verdana";
        design.Settings.PrimaryFontSize = 9f;
        config.PosterConfigurations.Add(design);

        Service().Initialize(config);

        Assert.Equal("Verdana", design.Settings.PrimaryFontFamily);
        Assert.Equal(9f, design.Settings.PrimaryFontSize);
        Assert.True(design.Settings.ShowPrimary);
    }
}
