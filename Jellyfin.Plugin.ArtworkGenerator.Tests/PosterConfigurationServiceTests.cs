using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    public void GetDesignsForSlot_ListsTheExtraDesignsAndSkipsDeletedOrRepeatedOnes()
    {
        var config = Config();
        var service = Service();
        service.Initialize(config);

        var standard = config.PosterConfigurations.Single(c => c.IsDefault);
        var anime = config.PosterConfigurations.Single(c => c.Name == "Anime");

        var assignment = new SlotAssignment { DesignId = anime.Id, SecondaryDesignId = standard.Id, TertiaryDesignId = Guid.NewGuid() };
        Assert.Equal(new[] { anime.Settings, standard.Settings }, service.GetDesignsForSlot(assignment));

        var repeated = new SlotAssignment { DesignId = anime.Id, SecondaryDesignId = anime.Id };
        Assert.Equal(new[] { anime.Settings }, service.GetDesignsForSlot(repeated));

        var deletedFirst = new SlotAssignment { DesignId = Guid.NewGuid(), TertiaryDesignId = anime.Id };
        Assert.Equal(new[] { standard.Settings, anime.Settings }, service.GetDesignsForSlot(deletedFirst));
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
    /// A logo design file that exists but cannot be read is the user's designs, damaged. It must
    /// never be replaced with a lone default, which would turn a repairable file into lost work.
    /// </summary>
    [Fact]
    public void Initialize_LeavesACorruptLogoFileAloneAndUsesTheDefaultInMemory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ag-logos-" + Guid.NewGuid().ToString("N") + ".json");
        const string Damaged = "{ \"Logos\": [ { \"Name\": \"Mine\", ";
        File.WriteAllText(path, Damaged);

        try
        {
            var service = new PosterConfigurationService(NullLogger<PosterConfigurationService>.Instance, new LogoDesignStore(path));
            service.Initialize(Config());

            var logo = Assert.Single(service.GetLogoDesigns());
            Assert.Equal(PosterConfigurationService.DefaultLogoDesignId, logo.Id);
            Assert.Equal(Damaged, File.ReadAllText(path));
            Assert.Equal(Damaged, File.ReadAllText(path + ".bak"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }

    /// <summary>A first run, with no logo design file yet, is given one with the default design.</summary>
    [Fact]
    public void Initialize_SeedsAMissingLogoFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "ag-logos-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            var service = new PosterConfigurationService(NullLogger<PosterConfigurationService>.Instance, new LogoDesignStore(path));
            service.Initialize(Config());

            Assert.True(File.Exists(path));
            var stored = Assert.Single(new LogoDesignStore(path).Load());
            Assert.Equal(PosterConfigurationService.DefaultLogoDesignId, stored.Id);
            Assert.False(File.Exists(path + ".bak"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A configuration posted without its lists, or with gaps in them, is filled in rather than
    /// failing every lookup and every start after it is saved.
    /// </summary>
    [Fact]
    public void Initialize_FillsInMissingListsAndSettings()
    {
        var config = new PluginConfiguration
        {
            PosterConfigurations = null!,
            Profiles = null!,
            FrameExtraction = null!
        };

        Service().Initialize(config);

        Assert.NotNull(config.FrameExtraction);
        Assert.Single(config.PosterConfigurations, c => c.IsDefault);
        var profile = Assert.Single(config.Profiles);
        Assert.True(profile.IsDefault);
    }

    [Fact]
    public void Initialize_FillsInAProfileOrDesignMissingItsParts()
    {
        var config = Config();
        config.PosterConfigurations.Add(null!);
        config.PosterConfigurations.Add(new PosterConfiguration { Name = "Bare", Settings = null! });
        config.Profiles.Add(null!);
        config.Profiles.Add(new ArtworkProfile
        {
            Name = "Partial",
            IsDefault = true,
            SeriesIds = null!,
            MovieIds = null!,
            VideoIds = null!,
            Slots = null!,
            Backdrop = null!
        });

        var service = Service();
        service.Initialize(config);

        Assert.DoesNotContain(null, config.PosterConfigurations);
        Assert.All(config.PosterConfigurations, c => Assert.NotNull(c.Settings));
        var profile = Assert.Single(config.Profiles);
        Assert.NotNull(profile.SeriesIds);
        Assert.NotNull(profile.Backdrop);
        Assert.Equal(ArtworkProfile.SupportedSlots.Count, profile.Slots.Count);
        Assert.Same(profile, service.GetProfileFor(ArtworkItemKind.Series, Guid.NewGuid()));
    }

    /// <summary>
    /// A logo save rebuilds the lookups while a configuration save may be doing the same. Run side
    /// by side, neither may fail or leave the configuration with repeated defaults or slots.
    /// </summary>
    [Fact]
    public void Initialize_AndSaveLogoDesigns_CanRunAtTheSameTime()
    {
        var service = Service();
        var logos = new[] { new LogoConfiguration { Name = "Default" } };

        // A fresh configuration each round, so every round races to fill in the same defaults.
        for (var round = 0; round < 200; round++)
        {
            var config = new PluginConfiguration();
            Parallel.For(0, 16, i =>
            {
                if (i % 2 == 0)
                {
                    service.Initialize(config);
                }
                else
                {
                    service.SaveLogoDesigns(config, logos);
                }
            });

            Assert.Single(config.PosterConfigurations, c => c.IsDefault);
            var profile = Assert.Single(config.Profiles);
            Assert.Equal(ArtworkProfile.SupportedSlots.Count, profile.Slots.Count);
        }
    }

}
