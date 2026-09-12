using System;
using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Services.Artwork;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

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


    /// <summary>
    /// Candidates come from as many different episodes as the batch allows: ranking purely by score
    /// let one episode supply nearly every image the picker offered.
    /// </summary>
    [Fact]
    public void InterleaveBySource_SpreadsConsecutiveRanksAcrossSources()
    {
        // One episode scores well throughout, which is exactly the case that used to crowd out the rest.
        var batch = new[]
        {
            ("a", 0.99), ("a", 0.98), ("a", 0.97), ("a", 0.96),
            ("b", 0.50), ("b", 0.40),
            ("c", 0.30)
        };

        var ordered = FramePoolService.InterleaveBySource(batch, f => f.Item1, f => f.Item2);

        Assert.Equal(new[] { "a", "b", "c", "a", "b", "a", "a" }, ordered.Select(f => f.Item1));

        // The first three ranks, which is what a four-candidate request shows first, are all different.
        Assert.Equal(3, ordered.Take(3).Select(f => f.Item1).Distinct().Count());
    }

    /// <summary>Every frame survives the reordering, and each source keeps its own best-first order.</summary>
    [Fact]
    public void InterleaveBySource_KeepsEveryFrameAndOrdersEachSourceByScore()
    {
        var batch = new[] { ("a", 0.1), ("b", 0.9), ("a", 0.8), ("b", 0.2) };

        var ordered = FramePoolService.InterleaveBySource(batch, f => f.Item1, f => f.Item2);

        Assert.Equal(batch.Length, ordered.Count);
        Assert.Equal(new[] { 0.8, 0.1 }, ordered.Where(f => f.Item1 == "a").Select(f => f.Item2));
        Assert.Equal(new[] { 0.9, 0.2 }, ordered.Where(f => f.Item1 == "b").Select(f => f.Item2));

        // The source holding the best frame leads.
        Assert.Equal("b", ordered[0].Item1);
    }

    /// <summary>A single source is left in plain score order.</summary>
    [Fact]
    public void InterleaveBySource_LeavesOneSourceInScoreOrder()
    {
        var batch = new[] { ("only", 0.2), ("only", 0.9), ("only", 0.5) };

        var ordered = FramePoolService.InterleaveBySource(batch, f => f.Item1, f => f.Item2);

        Assert.Equal(new[] { 0.9, 0.5, 0.2 }, ordered.Select(f => f.Item2));
    }


    /// <summary>
    /// The source order is shuffled, but deterministically: the same pool seed must pick the same
    /// episodes every time, or a season's artwork would be drawn from different episodes on every
    /// server restart.
    /// </summary>
    [Fact]
    public void OrderSources_IsDeterministicForASeed()
    {
        var sources = new[] { "a", "b", "c", "d", "e", "f", "g", "h" };

        var first = FramePoolService.OrderSources(sources, 1234).ToList();
        var again = FramePoolService.OrderSources(sources, 1234).ToList();

        Assert.Equal(first, again);
    }

    /// <summary>Every source survives the shuffle; none is dropped or repeated.</summary>
    [Fact]
    public void OrderSources_KeepsEverySourceExactlyOnce()
    {
        var sources = new[] { "a", "b", "c", "d", "e", "f", "g", "h" };

        var ordered = FramePoolService.OrderSources(sources, 99).ToList();

        Assert.Equal(sources.Length, ordered.Count);
        Assert.Equal(sources.OrderBy(s => s), ordered.OrderBy(s => s));
    }

    /// <summary>
    /// Different seeds generally give different orders, which is what stops every season of a show
    /// from sampling the same episodes.
    /// </summary>
    [Fact]
    public void OrderSources_VariesAcrossSeeds()
    {
        var sources = new[] { "a", "b", "c", "d", "e", "f", "g", "h" };

        var orders = Enumerable.Range(0, 12)
            .Select(seed => string.Join("", FramePoolService.OrderSources(sources, seed)))
            .Distinct()
            .Count();

        Assert.True(orders > 1, "Twelve seeds produced a single ordering, so the seed is being ignored.");
    }

    /// <summary>An empty or single-item list is handled without special-casing by the caller.</summary>
    [Fact]
    public void OrderSources_HandlesTrivialLists()
    {
        Assert.Empty(FramePoolService.OrderSources(Array.Empty<string>(), 7));
        Assert.Equal(new[] { "only" }, FramePoolService.OrderSources(new[] { "only" }, 7));
    }
}
