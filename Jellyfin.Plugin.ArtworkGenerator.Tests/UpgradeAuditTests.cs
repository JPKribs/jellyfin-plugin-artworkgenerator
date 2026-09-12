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
/// Pins what a configuration written before profiles turns on once it is upgraded. These slots
/// decide how much work the plugin starts doing on a library it was already installed on, so a
/// change here is a change to every existing user's first refresh and should be deliberate.
/// </summary>
public class UpgradeAuditTests
{
    private static ArtworkProfile UpgradeLegacyConfig()
    {
        var config = new PluginConfiguration();
        config.PosterConfigurations.Add(new PosterConfiguration { Name = "Default", IsDefault = true });

        new PosterConfigurationService(NullLogger<PosterConfigurationService>.Instance).Initialize(config);

        return config.Profiles.Single(p => p.IsDefault);
    }

    /// <summary>
    /// The episode poster is what the user already had, so it stays on.
    /// </summary>
    [Fact]
    public void Upgrading_KeepsTheEpisodePosterOn()
    {
        Assert.True(UpgradeLegacyConfig().GetSlot(ArtworkItemKind.Episode, ArtworkSlot.Primary)?.Enabled);
    }

    /// <summary>
    /// Series and season artwork is switched on by the upgrade, so an existing library starts
    /// generating six image types it was never asked for until the user turns them off.
    /// </summary>
    [Theory]
    [InlineData(ArtworkItemKind.Series, ArtworkSlot.Primary)]
    [InlineData(ArtworkItemKind.Series, ArtworkSlot.Thumb)]
    [InlineData(ArtworkItemKind.Series, ArtworkSlot.Logo)]
    [InlineData(ArtworkItemKind.Series, ArtworkSlot.Backdrop)]
    [InlineData(ArtworkItemKind.Season, ArtworkSlot.Primary)]
    [InlineData(ArtworkItemKind.Season, ArtworkSlot.Thumb)]
    public void Upgrading_TurnsOnTheNewShowArtwork(ArtworkItemKind kind, ArtworkSlot slot)
    {
        Assert.True(UpgradeLegacyConfig().GetSlot(kind, slot)?.Enabled);
    }

    /// <summary>
    /// Films and standalone videos stay off, so a library of movies generates nothing until the
    /// user opts in. A fresh install gets the same answer, which is why the film artwork this
    /// release adds does not appear on its own.
    /// </summary>
    [Theory]
    [InlineData(ArtworkItemKind.Movie)]
    [InlineData(ArtworkItemKind.Video)]
    public void Upgrading_LeavesFilmsAndVideosOff(ArtworkItemKind kind)
    {
        var profile = UpgradeLegacyConfig();
        Assert.All(
            ArtworkProfile.SupportedSlots.Where(s => s.Kind == kind),
            s => Assert.False(profile.GetSlot(s.Kind, s.Slot)?.Enabled));
    }
}
