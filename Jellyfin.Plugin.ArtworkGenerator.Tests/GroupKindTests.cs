using System;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.ArtworkGenerator.Configuration;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services;
using Jellyfin.Plugin.ArtworkGenerator.Services.Artwork;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Playlists;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

public class GroupKindTests
{
    [Fact]
    public void ACollectionIsACollection()
    {
        Assert.Equal(ArtworkItemKind.Collection, ArtworkService.GetKind(new BoxSet()));
    }

    [Fact]
    public void APlainFolderIsAFolder()
    {
        Assert.Equal(ArtworkItemKind.Folder, ArtworkService.GetKind(new Folder()));
    }

    [Fact]
    public void ALibraryIsALibrary()
    {
        Assert.Equal(ArtworkItemKind.Library, ArtworkService.GetKind(new CollectionFolder()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(CollectionType.movies)]
    [InlineData(CollectionType.tvshows)]
    [InlineData(CollectionType.homevideos)]
    [InlineData(CollectionType.musicvideos)]
    [InlineData(CollectionType.boxsets)]
    [InlineData(CollectionType.folders)]
    public void ALibraryThatCanHoldVideoIsDrawn(CollectionType? type)
    {
        Assert.Equal(ArtworkItemKind.Library, ArtworkService.GetKind(new CollectionFolder { CollectionType = type }));
    }

    [Theory]
    [InlineData(CollectionType.music)]
    [InlineData(CollectionType.books)]
    [InlineData(CollectionType.photos)]
    [InlineData(CollectionType.livetv)]
    [InlineData(CollectionType.playlists)]
    public void ALibraryThatCannotHoldVideoIsNotDrawn(CollectionType type)
    {
        Assert.Null(ArtworkService.GetKind(new CollectionFolder { CollectionType = type }));
    }

    [Fact]
    public void FromItemAgreesWithGetKind()
    {
        Assert.Throws<ArgumentException>(() => ArtworkSubject.FromItem(new CollectionFolder { CollectionType = CollectionType.music }));
        Assert.Throws<ArgumentException>(() => ArtworkSubject.FromItem(new Playlist()));
    }

    [Fact]
    public void AGroupSubjectIsBuiltWithoutWalkingItsVideos()
    {
        var folder = new Folder { Name = "Holidays", SortName = "Holidays", Path = "/media/home/Holidays" };

        var subject = ArtworkSubject.FromItem(folder);

        Assert.Equal(ArtworkItemKind.Folder, subject.Kind);
        Assert.Equal("Holidays", subject.Primary);
        Assert.Equal("Holidays", subject.FolderName);
        Assert.Equal("/media/home/Holidays", subject.VideoMetadata.SourcePath);
    }

    [Fact]
    public void AVideoOnDiskIsItsOwnSourceAndAMissingOneIsNot()
    {
        var path = System.IO.Path.GetTempFileName();
        try
        {
            Assert.Single(ArtworkSources.GetPlayableSources(new Video { Path = path }));
            Assert.Single(ArtworkSources.GetPlayableSources(new Movie { Path = path }));
            Assert.Empty(ArtworkSources.GetPlayableSources(new Video { Path = path + ".missing" }));
            Assert.Empty(ArtworkSources.GetPlayableSources(new Video { Path = path, IsVirtualItem = true }));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void SpecialisedFoldersAreNotDrawn()
    {
        Assert.Null(ArtworkService.GetKind(new UserRootFolder()));
        Assert.Null(ArtworkService.GetKind(new AggregateFolder()));
        Assert.Null(ArtworkService.GetKind(new Playlist()));
        Assert.Null(ArtworkService.GetKind(new PhotoAlbum()));
    }

    [Theory]
    [InlineData(ArtworkItemKind.Collection)]
    [InlineData(ArtworkItemKind.Folder)]
    [InlineData(ArtworkItemKind.Library)]
    public void GroupsAreAssignedByTheirOwnId(ArtworkItemKind kind)
    {
        Assert.True(kind.IsGroup());
        Assert.True(kind.IsAssignedByOwnId());
        Assert.False(kind.IsStandalone());
    }

    [Theory]
    [InlineData(ArtworkItemKind.Series)]
    [InlineData(ArtworkItemKind.Season)]
    [InlineData(ArtworkItemKind.Episode)]
    public void SeriesKindsAreAssignedByTheirSeries(ArtworkItemKind kind)
    {
        Assert.False(kind.IsGroup());
        Assert.False(kind.IsAssignedByOwnId());
    }

    [Theory]
    [InlineData(ArtworkItemKind.Collection)]
    [InlineData(ArtworkItemKind.Folder)]
    public void EveryImageIsOfferedForAGroup(ArtworkItemKind kind)
    {
        var offered = ArtworkProfile.SupportedSlots
            .Where(s => s.Kind == kind)
            .Select(s => s.Slot)
            .ToList();

        Assert.Equal(
            new[] { ArtworkSlot.Primary, ArtworkSlot.Thumb, ArtworkSlot.Logo, ArtworkSlot.Backdrop },
            offered);
    }

    [Fact]
    public void AGroupReadsByItsNameAlone()
    {
        var subject = new ArtworkSubject
        {
            Kind = ArtworkItemKind.Collection,
            SeriesName = "Alien Collection",
            ProductionYear = 1979
        };

        Assert.Equal("Alien Collection", subject.Primary);
        Assert.Equal(string.Empty, subject.Secondary);
        Assert.Equal(string.Empty, subject.SecondaryShort);
        Assert.Null(subject.FeaturedNumber);
        Assert.True(subject.CutoutIsPrimary);
    }

    [Theory]
    [InlineData(ArtworkItemKind.Collection, ArtworkShape.Portrait)]
    [InlineData(ArtworkItemKind.Folder, ArtworkShape.Landscape)]
    [InlineData(ArtworkItemKind.Library, ArtworkShape.Landscape)]
    public void GroupsHaveTheirOwnPrimaryShape(ArtworkItemKind kind, ArtworkShape expected)
    {
        Assert.Equal(expected, new ArtworkProfile().GetShape(kind, ArtworkSlot.Primary));
    }

    [Theory]
    [InlineData(ArtworkItemKind.Collection)]
    [InlineData(ArtworkItemKind.Folder)]
    [InlineData(ArtworkItemKind.Library)]
    public void AnAssignedGroupUsesItsProfile(ArtworkItemKind kind)
    {
        var config = new PluginConfiguration();
        var service = new PosterConfigurationService(NullLogger<PosterConfigurationService>.Instance);
        service.Initialize(config);

        var custom = new ArtworkProfile { Name = "Custom" };
        var id = Guid.NewGuid();
        var assigned = kind switch
        {
            ArtworkItemKind.Collection => custom.CollectionIds,
            ArtworkItemKind.Folder => custom.FolderIds,
            _ => custom.LibraryIds
        };
        assigned.Add(id);
        config.Profiles.Add(custom);
        service.Initialize(config);

        Assert.Equal(custom.Id, service.GetProfileFor(kind, id).Id);
        Assert.True(service.GetProfileFor(kind, Guid.NewGuid()).IsDefault);
    }

    [Fact]
    public void ExistingProfilesGainTheNewKindsSwitchedOff()
    {
        var config = new PluginConfiguration();
        var service = new PosterConfigurationService(NullLogger<PosterConfigurationService>.Instance);
        service.Initialize(config);

        var profile = config.Profiles.Single(p => p.IsDefault);
        var added = ArtworkProfile.SupportedSlots.Where(s => s.Kind.IsGroup()).ToList();

        Assert.Equal(10, added.Count);
        Assert.All(added, s => Assert.False(profile.GetSlot(s.Kind, s.Slot)!.Enabled));
    }

    [Fact]
    public void ALibraryOffersPrimaryAndThumbOnly()
    {
        var offered = ArtworkProfile.SupportedSlots
            .Where(s => s.Kind == ArtworkItemKind.Library)
            .Select(s => s.Slot)
            .ToList();

        Assert.Equal(new[] { ArtworkSlot.Primary, ArtworkSlot.Thumb }, offered);
    }

    [Theory]
    [InlineData(5, 200)]
    [InlineData(200, 200)]
    [InlineData(1000, 200)]
    [InlineData(4321, 200)]
    public void ALargeGroupIsVisitedSpreadAcrossItsWholeLengthFirst(int total, int perPass)
    {
        var items = Enumerable.Range(0, total).ToList();

        var order = ArtworkSources.SpreadOrder(items, perPass).ToList();
        var stride = (int)Math.Ceiling(total / (double)perPass);
        var firstPass = order.Take((int)Math.Ceiling(total / (double)stride)).ToList();

        Assert.Equal(items, order.OrderBy(i => i));
        Assert.True(firstPass.Count <= perPass);
        Assert.Equal(0, firstPass[0]);
        Assert.True(firstPass[^1] >= total - stride);
        Assert.All(firstPass, i => Assert.Equal(0, i % stride));
    }

    [Fact]
    public void AnOfflineStretchDoesNotEmptyTheSample()
    {
        var items = Enumerable.Range(0, 1000).ToList();
        var readable = new System.Collections.Generic.HashSet<int>(Enumerable.Range(0, 1000).Where(i => i % 5 == 3));

        var kept = ArtworkSources.SpreadOrder(items, 200)
            .Take(ArtworkSources.MaxGroupChecks)
            .Where(readable.Contains)
            .Take(ArtworkSources.MaxGroupSources)
            .ToList();

        Assert.Equal(200, kept.Count);
    }
}
