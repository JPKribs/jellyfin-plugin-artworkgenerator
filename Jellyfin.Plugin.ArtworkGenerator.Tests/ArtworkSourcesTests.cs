using System;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// A season's own children come back empty in some libraries, which left every season poster
/// falling back to the series backdrop even though its episodes extracted perfectly well on their
/// own. The season now walks its series instead, and these hold the rule that decides which of the
/// series' episodes are the season's.
/// </summary>
public class ArtworkSourcesTests
{
    [Fact]
    public void BelongsTo_MatchesTheSeasonTheEpisodeIsLinkedTo()
    {
        var season = new Season { Id = Guid.NewGuid(), IndexNumber = 2 };
        var episode = new Episode { SeasonId = season.Id, ParentIndexNumber = 99 };

        // The link wins over the number: a mislabelled ParentIndexNumber must not unseat it.
        Assert.True(ArtworkSources.BelongsTo(episode, season));
    }

    [Fact]
    public void BelongsTo_RejectsAnEpisodeLinkedToAnotherSeason()
    {
        var season = new Season { Id = Guid.NewGuid(), IndexNumber = 1 };
        var episode = new Episode { SeasonId = Guid.NewGuid(), ParentIndexNumber = 1 };

        Assert.False(ArtworkSources.BelongsTo(episode, season));
    }

    [Fact]
    public void BelongsTo_FallsBackToTheSeasonNumberWhenTheEpisodeHasNoLink()
    {
        var season = new Season { Id = Guid.NewGuid(), IndexNumber = 3 };

        Assert.True(ArtworkSources.BelongsTo(new Episode { ParentIndexNumber = 3 }, season));
        Assert.False(ArtworkSources.BelongsTo(new Episode { ParentIndexNumber = 4 }, season));
    }

    /// <summary>
    /// With neither a link nor a season number there is nothing to match on, and guessing would
    /// hand a season frames from episodes that are not its own.
    /// </summary>
    [Fact]
    public void BelongsTo_MatchesNothingWhenTheSeasonHasNoNumberAndTheEpisodeNoLink()
    {
        var season = new Season { Id = Guid.NewGuid() };

        Assert.False(ArtworkSources.BelongsTo(new Episode { ParentIndexNumber = 1 }, season));
    }
}
