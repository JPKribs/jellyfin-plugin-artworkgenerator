using System;
using Jellyfin.Plugin.EpisodePosterGenerator.Services.Artwork;
using Xunit;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Tests;

/// <summary>
/// Tests for the deterministic pieces of frame pool seeding. With a fixed seed, the same item must
/// reproduce the same frames across restarts, which rests on these being stable.
/// </summary>
public class FramePoolServiceTests
{
    [Fact]
    public void StableHash_IsDeterministicAndKeySensitive()
    {
        Assert.Equal(FramePoolService.StableHash("item:20-80"), FramePoolService.StableHash("item:20-80"));
        Assert.NotEqual(FramePoolService.StableHash("item:20-80"), FramePoolService.StableHash("item:10-80"));
    }

    [Fact]
    public void PhaseFor_IsDeterministicAndInRange()
    {
        foreach (var seed in new[] { 0, 42, -7, int.MaxValue })
        {
            for (int source = 0; source < 3; source++)
            {
                var phase = FramePoolService.PhaseFor(seed, source);
                Assert.InRange(phase, 0.0, 0.9999999);
                Assert.Equal(phase, FramePoolService.PhaseFor(seed, source));
            }
        }

        Assert.NotEqual(FramePoolService.PhaseFor(42, 0), FramePoolService.PhaseFor(42, 1));
    }

    /// <summary>
    /// Different extraction windows sample different parts of the video, so they cannot share a pool.
    /// </summary>
    [Fact]
    public void KeyFor_SeparatesItemsAndWindows()
    {
        var id = Guid.NewGuid();

        Assert.Equal(FramePoolService.KeyFor(id, 20, 80), FramePoolService.KeyFor(id, 20, 80));
        Assert.NotEqual(FramePoolService.KeyFor(id, 20, 80), FramePoolService.KeyFor(id, 10, 80));
        Assert.NotEqual(FramePoolService.KeyFor(id, 20, 80), FramePoolService.KeyFor(Guid.NewGuid(), 20, 80));
    }
}
