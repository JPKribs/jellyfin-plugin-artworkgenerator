using System;
using Jellyfin.Plugin.ArtworkGenerator.Configuration;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Every profile covers every kind of item; which items it draws is decided by assignment alone.
/// </summary>
public class ProfileAssignmentTests
{
    private static PosterConfigurationService Service() => new(NullLogger<PosterConfigurationService>.Instance);

    private static PluginConfiguration WithDefault()
    {
        var config = new PluginConfiguration();
        config.Profiles.Add(new ArtworkProfile { Name = "Default", IsDefault = true });
        return config;
    }

    /// <summary>An unassigned item of any kind falls to the default profile.</summary>
    [Theory]
    [InlineData(ArtworkItemKind.Series)]
    [InlineData(ArtworkItemKind.Season)]
    [InlineData(ArtworkItemKind.Episode)]
    [InlineData(ArtworkItemKind.Movie)]
    public void GetProfileFor_FallsBackToTheDefaultForEveryKind(ArtworkItemKind kind)
    {
        var config = WithDefault();
        var service = Service();
        service.Initialize(config);

        var profile = service.GetProfileFor(kind, Guid.NewGuid());

        Assert.NotNull(profile);
        Assert.True(profile.IsDefault);
    }

    /// <summary>A film assigned to a profile gets that profile rather than the default.</summary>
    [Fact]
    public void GetProfileFor_PrefersTheProfileAFilmIsAssignedTo()
    {
        var movieId = Guid.NewGuid();
        var config = WithDefault();

        var custom = new ArtworkProfile { Name = "Films" };
        custom.MovieIds.Add(movieId);
        config.Profiles.Add(custom);

        var service = Service();
        service.Initialize(config);

        Assert.Equal(custom.Id, service.GetProfileFor(ArtworkItemKind.Movie, movieId).Id);
    }

    /// <summary>A series assigned to a profile gets that profile rather than the default.</summary>
    [Fact]
    public void GetProfileFor_PrefersTheProfileASeriesIsAssignedTo()
    {
        var seriesId = Guid.NewGuid();
        var config = WithDefault();

        var custom = new ArtworkProfile { Name = "Anime" };
        custom.SeriesIds.Add(seriesId);
        config.Profiles.Add(custom);

        var service = Service();
        service.Initialize(config);

        Assert.Equal(custom.Id, service.GetProfileFor(ArtworkItemKind.Series, seriesId).Id);
    }

    /// <summary>
    /// One profile can carry both series and films, since a profile is no longer limited to a
    /// kind of library.
    /// </summary>
    [Fact]
    public void AProfileCanCarryBothSeriesAndFilms()
    {
        var seriesId = Guid.NewGuid();
        var movieId = Guid.NewGuid();
        var config = WithDefault();

        var custom = new ArtworkProfile { Name = "Everything" };
        custom.SeriesIds.Add(seriesId);
        custom.MovieIds.Add(movieId);
        config.Profiles.Add(custom);

        var service = Service();
        service.Initialize(config);

        Assert.Equal(custom.Id, service.GetProfileFor(ArtworkItemKind.Series, seriesId).Id);
        Assert.Equal(custom.Id, service.GetProfileFor(ArtworkItemKind.Movie, movieId).Id);
    }

    /// <summary>Every slot a film can carry is offered, matching a series.</summary>
    [Theory]
    [InlineData(ArtworkSlot.Primary)]
    [InlineData(ArtworkSlot.Thumb)]
    [InlineData(ArtworkSlot.Logo)]
    [InlineData(ArtworkSlot.Backdrop)]
    public void Movies_OfferEverySlot(ArtworkSlot slot)
    {
        Assert.True(ArtworkProfile.IsSupported(ArtworkItemKind.Movie, slot));
    }
}
