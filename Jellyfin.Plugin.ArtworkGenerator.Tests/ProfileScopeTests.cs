using System;
using Jellyfin.Plugin.ArtworkGenerator.Configuration;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// A profile says what it is for. These pin the rule that matters most: a profile written before
/// films were supported must never quietly start claiming a library's films.
/// </summary>
public class ProfileScopeTests
{
    private static PosterConfigurationService Service() => new(NullLogger<PosterConfigurationService>.Instance);

    /// <summary>A profile with no scope saved against it is a TV profile, which is what it was.</summary>
    [Fact]
    public void NewProfile_DefaultsToTv()
    {
        Assert.Equal(ProfileScope.Tv, new ArtworkProfile().Scope);
    }

    [Theory]
    [InlineData(ProfileScope.Tv, ArtworkItemKind.Series, true)]
    [InlineData(ProfileScope.Tv, ArtworkItemKind.Season, true)]
    [InlineData(ProfileScope.Tv, ArtworkItemKind.Episode, true)]
    [InlineData(ProfileScope.Tv, ArtworkItemKind.Movie, false)]
    [InlineData(ProfileScope.Movies, ArtworkItemKind.Series, false)]
    [InlineData(ProfileScope.Movies, ArtworkItemKind.Movie, true)]
    [InlineData(ProfileScope.Both, ArtworkItemKind.Series, true)]
    [InlineData(ProfileScope.Both, ArtworkItemKind.Movie, true)]
    public void AppliesTo_FollowsTheScope(ProfileScope scope, ArtworkItemKind kind, bool expected)
    {
        Assert.Equal(expected, new ArtworkProfile { Scope = scope }.AppliesTo(kind));
    }

    /// <summary>
    /// The upgrade case: a configuration whose only profile is the TV default leaves films alone
    /// rather than sweeping them into whatever that profile happens to draw.
    /// </summary>
    [Fact]
    public void GetProfileFor_DeclinesFilmsUntilAProfileCoversThem()
    {
        var config = new PluginConfiguration();
        config.Profiles.Add(new ArtworkProfile { Name = "Default", IsDefault = true, Scope = ProfileScope.Tv });
        var service = Service();
        service.Initialize(config);

        Assert.Null(service.GetProfileFor(ArtworkItemKind.Movie, Guid.NewGuid()));
        Assert.NotNull(service.GetProfileFor(ArtworkItemKind.Series, Guid.NewGuid()));
    }

    /// <summary>Widening the default to Both is all it takes for films to be covered.</summary>
    [Fact]
    public void GetProfileFor_UsesTheDefaultWhenItsScopeCoversFilms()
    {
        var config = new PluginConfiguration();
        config.Profiles.Add(new ArtworkProfile { Name = "Default", IsDefault = true, Scope = ProfileScope.Both });
        var service = Service();
        service.Initialize(config);

        Assert.NotNull(service.GetProfileFor(ArtworkItemKind.Movie, Guid.NewGuid()));
    }

    /// <summary>A film assigned to a Movies profile gets that profile rather than the default.</summary>
    [Fact]
    public void GetProfileFor_PrefersTheProfileAFilmIsAssignedTo()
    {
        var movieId = Guid.NewGuid();
        var config = new PluginConfiguration();
        config.Profiles.Add(new ArtworkProfile { Name = "Default", IsDefault = true, Scope = ProfileScope.Both });

        var films = new ArtworkProfile { Name = "Films", Scope = ProfileScope.Movies };
        films.MovieIds.Add(movieId);
        config.Profiles.Add(films);

        var service = Service();
        service.Initialize(config);

        Assert.Equal(films.Id, service.GetProfileFor(ArtworkItemKind.Movie, movieId)!.Id);
    }

    /// <summary>
    /// A TV profile's film list is not honoured: the scope decides, so a stale assignment left over
    /// from flipping a profile back to TV cannot keep claiming a film.
    /// </summary>
    [Fact]
    public void GetProfileFor_IgnoresFilmsAssignedToATvProfile()
    {
        var movieId = Guid.NewGuid();
        var config = new PluginConfiguration();
        config.Profiles.Add(new ArtworkProfile { Name = "Default", IsDefault = true, Scope = ProfileScope.Both });

        var tv = new ArtworkProfile { Name = "Shows", Scope = ProfileScope.Tv };
        tv.MovieIds.Add(movieId);
        config.Profiles.Add(tv);

        var service = Service();
        service.Initialize(config);

        var resolved = service.GetProfileFor(ArtworkItemKind.Movie, movieId);
        Assert.NotNull(resolved);
        Assert.NotEqual(tv.Id, resolved!.Id);
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
