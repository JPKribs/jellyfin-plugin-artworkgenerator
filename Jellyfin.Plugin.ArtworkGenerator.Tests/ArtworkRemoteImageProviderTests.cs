using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Providers;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Tests for telling the Edit Images dialog apart from a metadata refresh, and for the address the
/// server uses to fetch its own generated images.
/// </summary>
public class ArtworkRemoteImageProviderTests
{
    [Theory]
    [InlineData("/Items/5d1a4a6bd5d84d5cb54b7c1e2d1f0a11/RemoteImages")]
    [InlineData("/Items/5d1a4a6bd5d84d5cb54b7c1e2d1f0a11/RemoteImages/")]
    [InlineData("/jellyfin/Items/5d1a4a6bd5d84d5cb54b7c1e2d1f0a11/remoteimages")]
    public void IsImagePickerRequest_RecognizesTheEditImagesRequest(string path)
    {
        Assert.True(ArtworkRemoteImageProvider.IsImagePickerRequest(path));
    }

    /// <summary>
    /// A refresh runs with no request, or inside one for something else entirely, and must be
    /// treated as a refresh.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/Items/5d1a4a6bd5d84d5cb54b7c1e2d1f0a11/Refresh")]
    [InlineData("/Items/5d1a4a6bd5d84d5cb54b7c1e2d1f0a11/RemoteImages/Providers")]
    [InlineData("/Items/5d1a4a6bd5d84d5cb54b7c1e2d1f0a11/RemoteImages/Download")]
    [InlineData("/Items/RemoteSearch/Apply/5d1a4a6bd5d84d5cb54b7c1e2d1f0a11")]
    public void IsImagePickerRequest_TreatsAnythingElseAsARefresh(string? path)
    {
        Assert.False(ArtworkRemoteImageProvider.IsImagePickerRequest(path));
    }

    [Theory]
    [InlineData(null, "http://127.0.0.1:8096")]
    [InlineData("", "http://127.0.0.1:8096")]
    [InlineData("/jellyfin", "http://127.0.0.1:8096/jellyfin")]
    [InlineData("jellyfin/", "http://127.0.0.1:8096/jellyfin")]
    [InlineData("/", "http://127.0.0.1:8096")]
    public void FallbackBaseUrl_KeepsTheServersBaseUrl(string? baseUrl, string expected)
    {
        Assert.Equal(expected, ArtworkRemoteImageProvider.FallbackBaseUrl(8096, baseUrl));
    }

    [Fact]
    public void FallbackBaseUrl_NeedsAPort()
    {
        Assert.Null(ArtworkRemoteImageProvider.FallbackBaseUrl(0, "/jellyfin"));
    }

    private static readonly ImageType[] Enabled = { ImageType.Primary, ImageType.Thumb, ImageType.Logo, ImageType.Backdrop };

    /// <summary>
    /// A refresh may be replacing images the item already has, or topping its backdrops up to the
    /// library's limit, so it renders every enabled type: one of each, and backdrops up to the limit.
    /// </summary>
    [Fact]
    public void Plan_ARefreshRendersEveryTypeOnceAndBackdropsUpToTheLimit()
    {
        var plan = ArtworkRemoteImageProvider.Plan(Enabled, picking: false, choiceCount: 5, backdropLimit: 3);

        Assert.Equal(
            new[] { (ImageType.Primary, 1), (ImageType.Thumb, 1), (ImageType.Logo, 1), (ImageType.Backdrop, 3) },
            plan);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(50, 10)]
    public void Plan_ARefreshDrawsAtLeastOneBackdropAndNoMoreThanTheCap(int limit, int expected)
    {
        var plan = ArtworkRemoteImageProvider.Plan(new[] { ImageType.Backdrop }, picking: false, choiceCount: 3, backdropLimit: limit);

        Assert.Equal(expected, plan.Single().Count);
    }

    /// <summary>The Edit Images dialog is offered the configured number of choices for every type.</summary>
    [Fact]
    public void Plan_ThePickerGetsTheConfiguredChoicesForEveryType()
    {
        var plan = ArtworkRemoteImageProvider.Plan(Enabled, picking: true, choiceCount: 4, backdropLimit: 1);

        Assert.Equal(Enabled, plan.Select(p => p.Type));
        Assert.All(plan, p => Assert.Equal(4, p.Count));
    }
}
