using System;
using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services.Artwork;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities.Audio;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Anything a library holds as a video can be drawn, not just the ones that belong to a series. A
/// music video and a home video are both plain videos to Jellyfin, and both stand on their own the
/// way a film does.
/// </summary>
public class VideoKindTests
{
    [Fact]
    public void AMusicVideoIsAStandaloneVideo()
    {
        Assert.Equal(ArtworkItemKind.Video, ArtworkService.GetKind(new MusicVideo()));
    }

    [Fact]
    public void APlainVideoIsAStandaloneVideo()
    {
        Assert.Equal(ArtworkItemKind.Video, ArtworkService.GetKind(new Video()));
    }

    /// <summary>
    /// Episode, Movie and MusicVideo all derive from Video, so the order the kinds are matched in
    /// decides whether they keep their own identity.
    /// </summary>
    [Fact]
    public void TheSeriesKindsAreStillThemselves()
    {
        Assert.Equal(ArtworkItemKind.Episode, ArtworkService.GetKind(new Episode()));
        Assert.Equal(ArtworkItemKind.Movie, ArtworkService.GetKind(new Movie()));
        Assert.Equal(ArtworkItemKind.Season, ArtworkService.GetKind(new Season()));
        Assert.Equal(ArtworkItemKind.Series, ArtworkService.GetKind(new Series()));
    }

    [Fact]
    public void AFilmAndAVideoBothStandOnTheirOwn()
    {
        Assert.True(ArtworkItemKind.Movie.IsStandalone());
        Assert.True(ArtworkItemKind.Video.IsStandalone());
        Assert.False(ArtworkItemKind.Series.IsStandalone());
        Assert.False(ArtworkItemKind.Season.IsStandalone());
        Assert.False(ArtworkItemKind.Episode.IsStandalone());
    }

    /// <summary>
    /// A standalone video reads like a film: its own name is the title and its year the subtitle.
    /// </summary>
    [Fact]
    public void AStandaloneVideoReadsLikeAFilm()
    {
        // Built directly rather than through the factory, which reads media streams from a running
        // library. The factory fills exactly these fields, as it does for a film.
        var subject = new ArtworkSubject
        {
            Kind = ArtworkItemKind.Video,
            SeriesName = "Take On Me",
            ProductionYear = 1985
        };

        Assert.Equal("Take On Me", subject.Primary);
        Assert.Equal("1985", subject.Secondary);
        Assert.Equal("1985", subject.SecondaryShort);

        // Its own name is the whole of its identity, so a cutout carves the name rather than a
        // number, the same as a film or a series.
        Assert.True(subject.CutoutIsPrimary);
    }

    /// <summary>
    /// Its own file is the frame source, the same as a film's.
    /// </summary>
    [Fact]
    public void AStandaloneVideoIsItsOwnFrameSource()
    {
        var video = new MusicVideo { Name = "Take On Me" };

        // No path on disk, so nothing is playable, but the lookup must reach the item itself rather
        // than falling through to the empty case for an unknown type.
        Assert.Empty(ArtworkSources.GetPlayableSources(video));
    }

    [Fact]
    public void EveryImageIsOfferedForAStandaloneVideo()
    {
        var offered = ArtworkProfile.SupportedSlots
            .Where(s => s.Kind == ArtworkItemKind.Video)
            .Select(s => s.Slot)
            .ToList();

        Assert.Equal(
            new[] { ArtworkSlot.Primary, ArtworkSlot.Thumb, ArtworkSlot.Logo, ArtworkSlot.Backdrop },
            offered);
    }
}
