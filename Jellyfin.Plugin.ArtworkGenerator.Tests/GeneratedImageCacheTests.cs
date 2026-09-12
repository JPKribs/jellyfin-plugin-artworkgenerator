using System;
using System.Collections.Generic;
using Jellyfin.Plugin.ArtworkGenerator.Services.Posters;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Tests for the store backing the generated image URLs offered to Jellyfin's image picker.
/// </summary>
public class GeneratedImageCacheTests
{
    private static GeneratedImageCache CreateCache(ManualTime? time = null, long maxBytes = GeneratedImageCache.DefaultMaxBytes, int minutes = 30)
        => new(NullLogger<GeneratedImageCache>.Instance, () => minutes, maxBytes, time ?? new ManualTime());

    [Fact]
    public void Add_ThenTryGet_ReturnsTheStoredBytes()
    {
        using var cache = CreateCache();
        var payload = new byte[] { 1, 2, 3, 4 };

        var token = cache.Add(payload);

        Assert.True(cache.TryGet(token, out var retrieved));
        Assert.Equal(payload, retrieved);
    }

    [Fact]
    public void Add_IssuesADistinctTokenPerImage()
    {
        using var cache = CreateCache();

        var first = cache.Add(new byte[] { 1 });
        var second = cache.Add(new byte[] { 1 });

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-real-token")]
    [InlineData("../../etc/passwd")]
    public void TryGet_RejectsUnknownTokens(string token)
    {
        using var cache = CreateCache();
        cache.Add(new byte[] { 1, 2, 3 });

        Assert.False(cache.TryGet(token, out var retrieved));
        Assert.Empty(retrieved);
    }

    /// <summary>
    /// The cache holds encoded images in memory, so it must stay within its size bound no matter
    /// how many picker dialogs are opened, dropping the oldest first.
    /// </summary>
    [Fact]
    public void Add_EvictsTheOldestImagesOnceOverTheSizeBound()
    {
        var time = new ManualTime();
        using var cache = CreateCache(time, maxBytes: 100);
        var tokens = new List<string>();

        for (var i = 0; i < 30; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            tokens.Add(cache.Add(new byte[10]));
        }

        Assert.InRange(cache.Bytes, 1, 100);
        Assert.True(cache.TryGet(tokens[^1], out _));
        Assert.False(cache.TryGet(tokens[0], out _));
    }

    [Fact]
    public void Add_KeepsAnImageLargerThanTheWholeBound()
    {
        using var cache = CreateCache(maxBytes: 10);

        var token = cache.Add(new byte[50]);

        Assert.True(cache.TryGet(token, out _));
    }

    [Fact]
    public void TryGet_DropsAnExpiredImageAndFreesItsBytes()
    {
        var time = new ManualTime();
        using var cache = CreateCache(time);
        var token = cache.Add(new byte[10]);

        time.Advance(TimeSpan.FromMinutes(31));

        Assert.False(cache.TryGet(token, out _));
        Assert.Equal(0, cache.Bytes);
    }

    /// <summary>A lifetime set below the minimum still keeps an image for the minimum.</summary>
    [Fact]
    public void Lifetime_IsNeverShorterThanTheMinimum()
    {
        var time = new ManualTime();
        using var cache = CreateCache(time, minutes: 1);
        var token = cache.Add(new byte[1]);

        time.Advance(TimeSpan.FromMinutes(GeneratedImageCache.MinLifetimeMinutes - 1));
        Assert.True(cache.TryGet(token, out _));

        time.Advance(TimeSpan.FromMinutes(2));
        Assert.False(cache.TryGet(token, out _));
    }

    [Fact]
    public void IsAlive_RequiresTheImageToOutlastTheMargin()
    {
        var time = new ManualTime();
        using var cache = CreateCache(time);
        var token = cache.Add(new byte[1]);

        time.Advance(TimeSpan.FromMinutes(29));

        Assert.True(cache.TryGet(token, out _));
        Assert.False(cache.IsAlive(token, TimeSpan.FromMinutes(2)));
        Assert.True(cache.IsAlive(token, TimeSpan.FromSeconds(30)));
    }

    /// <summary>A clock the test moves by hand.</summary>
    internal sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
