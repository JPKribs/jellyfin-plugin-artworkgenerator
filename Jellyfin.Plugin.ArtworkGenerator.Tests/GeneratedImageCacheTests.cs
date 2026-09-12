using System.Collections.Generic;
using Jellyfin.Plugin.ArtworkGenerator.Services.Posters;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Tests for the store backing the generated poster URLs offered to Jellyfin's image picker.
/// </summary>
public class GeneratedImageCacheTests
{
    private static GeneratedImageCache CreateCache()
        => new(NullLogger<GeneratedImageCache>.Instance);

    [Fact]
    public void Add_ThenTryGet_ReturnsTheStoredBytes()
    {
        var cache = CreateCache();
        var payload = new byte[] { 1, 2, 3, 4 };

        var token = cache.Add(payload);

        Assert.True(cache.TryGet(token, out var retrieved));
        Assert.Equal(payload, retrieved);
    }

    [Fact]
    public void Add_IssuesADistinctTokenPerImage()
    {
        var cache = CreateCache();

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
        var cache = CreateCache();
        cache.Add(new byte[] { 1, 2, 3 });

        Assert.False(cache.TryGet(token, out var retrieved));
        Assert.Empty(retrieved);
    }

    /// <summary>
    /// The cache holds decoded posters in memory, so it must stay bounded no matter how many
    /// picker dialogs are opened.
    /// </summary>
    [Fact]
    public void Add_EvictsOldestEntriesOnceFull()
    {
        var cache = CreateCache();
        var tokens = new List<string>();

        for (var i = 0; i < 200; i++)
        {
            tokens.Add(cache.Add(new byte[] { (byte)i }));
        }

        var live = 0;
        foreach (var token in tokens)
        {
            if (cache.TryGet(token, out _)) live++;
        }

        Assert.InRange(live, 1, 64);

        // The most recent additions are the ones that survive.
        Assert.True(cache.TryGet(tokens[^1], out _));
        Assert.False(cache.TryGet(tokens[0], out _));
    }
}
