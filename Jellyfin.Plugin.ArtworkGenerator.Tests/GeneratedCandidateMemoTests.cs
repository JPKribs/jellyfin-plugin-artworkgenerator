using System;
using Jellyfin.Plugin.ArtworkGenerator.Services.Posters;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Tests for reusing the choices already rendered for an item in Edit Images.
/// </summary>
public class GeneratedCandidateMemoTests
{
    private static GeneratedImageCache Cache(GeneratedImageCacheTests.ManualTime time)
        => new(NullLogger<GeneratedImageCache>.Instance, () => 30, GeneratedImageCache.DefaultMaxBytes, time);

    private static RemoteImageInfo[] Images() => new[] { new RemoteImageInfo { Url = "u" } };

    [Fact]
    public void TryGet_ReturnsTheSetRenderedUnderTheSameStamp()
    {
        var time = new GeneratedImageCacheTests.ManualTime();
        using var cache = Cache(time);
        var memo = new GeneratedCandidateMemo();
        var item = Guid.NewGuid();
        var images = Images();

        memo.Set(item, "a", images, new[] { cache.Add(new byte[1]) });

        Assert.True(memo.TryGet(item, "a", cache, out var remembered));
        Assert.Same(images, remembered);
    }

    [Fact]
    public void TryGet_MissesOnceTheStampChanges()
    {
        var time = new GeneratedImageCacheTests.ManualTime();
        using var cache = Cache(time);
        var memo = new GeneratedCandidateMemo();
        var item = Guid.NewGuid();

        memo.Set(item, "a", Images(), new[] { cache.Add(new byte[1]) });

        Assert.False(memo.TryGet(item, "b", cache, out _));
        Assert.False(memo.TryGet(item, "a", cache, out _));
    }

    /// <summary>A remembered choice that could expire before it is picked is not offered again.</summary>
    [Fact]
    public void TryGet_MissesWhenAnImageIsAboutToExpire()
    {
        var time = new GeneratedImageCacheTests.ManualTime();
        using var cache = Cache(time);
        var memo = new GeneratedCandidateMemo();
        var item = Guid.NewGuid();

        memo.Set(item, "a", Images(), new[] { cache.Add(new byte[1]) });
        time.Advance(TimeSpan.FromMinutes(29));

        Assert.False(memo.TryGet(item, "a", cache, out _));
    }

    [Fact]
    public void Set_DoesNotRememberAnEmptySet()
    {
        var time = new GeneratedImageCacheTests.ManualTime();
        using var cache = Cache(time);
        var memo = new GeneratedCandidateMemo();
        var item = Guid.NewGuid();

        memo.Set(item, "a", Array.Empty<RemoteImageInfo>(), Array.Empty<string>());

        Assert.False(memo.TryGet(item, "a", cache, out _));
    }
}
