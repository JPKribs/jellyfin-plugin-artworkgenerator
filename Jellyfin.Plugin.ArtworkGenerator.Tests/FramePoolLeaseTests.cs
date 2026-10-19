using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ArtworkGenerator.Services.Artwork;
using Jellyfin.Plugin.ArtworkGenerator.Services;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Tests for how frame pools are held, grown, and discarded: a pool must survive while anyone holds
/// it, go once nobody does, and only look for its item's videos when it actually has to extract.
/// </summary>
public sealed class FramePoolLeaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ag-pool-" + Guid.NewGuid().ToString("N"));
    private readonly FramePoolService _pools;

    public FramePoolLeaseTests()
    {
        // Nothing here reaches ffmpeg: a source with no path is refused before extraction starts.
        var extractor = new FrameExtractionService(NullLogger<FrameExtractionService>.Instance, null!, null!, null!, null!);
        _pools = new FramePoolService(NullLogger<FramePoolService>.Instance, extractor, _root, () => 7);
    }

    public void Dispose()
    {
        _pools.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private int PoolsOnDisk => Directory.GetDirectories(_root).Length;

    // A video that cannot be extracted from, so growing the pool runs and comes back empty.
    private static IReadOnlyList<Video> Unreadable() => new[] { new Video { Name = "Missing" } };

    private Task<FrameLease> Acquire(string key, Func<IReadOnlyList<Video>> sources, int needed)
        => _pools.AcquireAsync(key, sources, 20, 80, needed, CancellationToken.None);

    [Fact]
    public async Task Acquire_DoesNotLookForSourcesWhenNoFramesAreNeeded()
    {
        var lookups = 0;

        using (var lease = await Acquire("item", () => { lookups++; return Unreadable(); }, 0))
        {
            Assert.Empty(lease.Paths);
        }

        Assert.Equal(0, lookups);
    }

    [Fact]
    public async Task Acquire_LooksForSourcesOnlyWhenThePoolMustGrow()
    {
        var lookups = 0;
        Func<IReadOnlyList<Video>> sources = () => { lookups++; return Unreadable(); };

        using (var first = await Acquire("item", sources, 2))
        {
            Assert.Empty(first.Paths);
        }

        Assert.Equal(1, lookups);

        // The first attempt found nothing to extract, so the pool is spent until it is discarded
        // and asking again neither extracts nor walks the item's videos again.
        using (var second = await Acquire("item", sources, 2))
        {
            Assert.Empty(second.Paths);
        }

        Assert.Equal(1, lookups);
    }

    [Fact]
    public async Task DiscardIdle_KeepsAHeldPoolAndDropsItOnceReleased()
    {
        var lease = await Acquire("item", Unreadable, 1);
        Assert.Equal(1, PoolsOnDisk);

        _pools.DiscardIdle();
        Assert.Equal(1, PoolsOnDisk);

        lease.Dispose();
        _pools.DiscardIdle();
        Assert.Equal(0, PoolsOnDisk);
    }

    [Fact]
    public async Task Lease_ReleasesOnlyOnceHoweverOftenItIsDisposed()
    {
        var first = await Acquire("item", Unreadable, 1);
        var second = await Acquire("item", Unreadable, 1);
        Assert.Equal(1, PoolsOnDisk);

        // A second dispose must not drop the other holder's reference too.
        first.Dispose();
        first.Dispose();
        _pools.DiscardIdle();
        Assert.Equal(1, PoolsOnDisk);

        second.Dispose();
        _pools.DiscardIdle();
        Assert.Equal(0, PoolsOnDisk);
    }

    [Fact]
    public async Task Release_DropsAPoolThatNeverExtracted()
    {
        // An item with no video gets a pool that never extracts anything. Kept, it would count
        // against the pool cap and push out pools that hold frames.
        using (var hold = await Acquire("no-video", () => Array.Empty<Video>(), 3))
        {
            Assert.Equal(1, PoolsOnDisk);
        }

        Assert.Equal(0, PoolsOnDisk);
    }

    [Fact]
    public async Task Release_KeepsAPoolAnotherCallerStillHolds()
    {
        var hold = await Acquire("item", Unreadable, 0);
        var lease = await Acquire("item", Unreadable, 0);

        hold.Dispose();
        Assert.Equal(1, PoolsOnDisk);

        lease.Dispose();
        Assert.Equal(0, PoolsOnDisk);
    }
}
