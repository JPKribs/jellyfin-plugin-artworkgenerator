using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ArtworkGenerator.Tasks;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Tests for choosing which backdrops the Cleanup Backdrops task removes, and when it may delete
/// their files. Only the plugin's own extra backdrops may go, and never a file something still uses.
/// </summary>
public sealed class CleanupBackdropsTaskTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ag-cleanup-" + Guid.NewGuid().ToString("N"));

    public CleanupBackdropsTaskTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
    }

    private static ItemImageInfo Backdrop(string path) => new() { Path = path, Type = ImageType.Backdrop };

    // WriteJpeg
    // A real JPEG on disk, tagged the way the plugin tags its backdrops or left as any other
    // program would write it.
    private string WriteJpeg(string name, bool generated)
    {
        using var bitmap = new SKBitmap(32, 18);
        bitmap.Erase(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);

        var bytes = data.ToArray();
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, generated ? GeneratedImageMarker.Tag(bytes) : bytes);
        return path;
    }

    [Fact]
    public void Extras_KeepsTheFirstBackdropAndReturnsTheRest()
    {
        var first = Backdrop("/m/backdrop.jpg");
        var second = Backdrop("/m/backdrop1.jpg");
        var third = Backdrop("/m/backdrop2.jpg");

        Assert.Equal(new[] { second, third }, CleanupBackdropsTask.Extras(new[] { first, second, third }));
    }

    [Fact]
    public void Extras_LeavesASingleBackdropAlone()
    {
        Assert.Empty(CleanupBackdropsTask.Extras(new[] { Backdrop("/m/backdrop.jpg") }));
    }

    /// <summary>
    /// Fanart the user put beside the media, or another provider's backdrop, is never removed,
    /// however many backdrops the item has.
    /// </summary>
    [Fact]
    public void Removable_LeavesBackdropsThePluginDidNotGenerate()
    {
        var kept = Backdrop(WriteJpeg("fanart.jpg", generated: false));
        var usersOwn = Backdrop(WriteJpeg("backdrop1.jpg", generated: false));
        var generated = Backdrop(WriteJpeg("backdrop2.jpg", generated: true));

        Assert.Equal(new[] { generated }, CleanupBackdropsTask.Removable(new[] { kept, usersOwn, generated }));
    }

    [Fact]
    public void Removable_AlwaysKeepsTheFirstBackdropEvenWhenGenerated()
    {
        var first = Backdrop(WriteJpeg("backdrop.jpg", generated: true));

        Assert.Empty(CleanupBackdropsTask.Removable(new[] { first }));
    }

    [Fact]
    public void IsGenerated_SkipsABackdropKnownOnlyByWebAddressOrMissingFromDisk()
    {
        Assert.False(CleanupBackdropsTask.IsGenerated(Backdrop("https://example.org/backdrop.jpg")));
        Assert.False(CleanupBackdropsTask.IsGenerated(Backdrop(Path.Combine(_folder, "gone.jpg"))));
        Assert.True(CleanupBackdropsTask.IsGenerated(Backdrop(WriteJpeg("ours.jpg", generated: true))));
    }

    /// <summary>
    /// A file the item's kept backdrop also points at stays on disk when the extra entry goes.
    /// </summary>
    [Fact]
    public void ReleaseReference_KeepsAFileTheKeptBackdropStillUses()
    {
        var references = new Dictionary<string, int>(StringComparer.Ordinal);
        CleanupBackdropsTask.AddReferences(references, new[] { Backdrop("/m/backdrop.jpg"), Backdrop("/m/backdrop.jpg"), Backdrop("/m/backdrop1.jpg") });

        Assert.False(CleanupBackdropsTask.ReleaseReference(references, "/m/backdrop.jpg"));
        Assert.True(CleanupBackdropsTask.ReleaseReference(references, "/m/backdrop1.jpg"));
    }

    /// <summary>
    /// A file shared by several items, such as fanart in a series folder, is only deleted once the
    /// last item using it lets go.
    /// </summary>
    [Fact]
    public void ReleaseReference_KeepsAFileAnotherItemStillUses()
    {
        var references = new Dictionary<string, int>(StringComparer.Ordinal);
        CleanupBackdropsTask.AddReferences(references, new[] { Backdrop("/show/backdrop.jpg"), Backdrop("/show/shared.jpg") });
        CleanupBackdropsTask.AddReferences(references, new[] { Backdrop("/show/season/backdrop.jpg"), Backdrop("/show/shared.jpg") });

        Assert.False(CleanupBackdropsTask.ReleaseReference(references, "/show/shared.jpg"));
        Assert.True(CleanupBackdropsTask.ReleaseReference(references, "/show/shared.jpg"));
    }

    [Fact]
    public void AddReferences_IgnoresImagesKnownOnlyByWebAddress()
    {
        var references = new Dictionary<string, int>(StringComparer.Ordinal);
        CleanupBackdropsTask.AddReferences(references, new[] { Backdrop("https://example.org/backdrop.jpg") });

        Assert.Empty(references);
        Assert.False(CleanupBackdropsTask.ReleaseReference(references, "https://example.org/backdrop.jpg"));
    }

    // RunAsync
    // Runs the whole task over the given items. The library answers a query for items with a
    // backdrop with only those, and any other query with every item, the way Jellyfin's would.
    private static async Task<List<BaseItem>> RunAsync(params BaseItem[] items)
    {
        var library = Substitute.For<ILibraryManager>();
        library.GetItemIds(Arg.Any<InternalItemsQuery>()).Returns(call =>
        {
            var query = call.Arg<InternalItemsQuery>();
            var types = query.ImageTypes ?? Array.Empty<ImageType>();
            return items
                .Where(item => types.Length == 0 || types.Any(type => item.ImageInfos.Any(image => image.Type == type)))
                .Select(item => item.Id)
                .ToList();
        });
        library.GetItemById(Arg.Any<Guid>()).Returns(call => items.FirstOrDefault(item => item.Id == call.Arg<Guid>()));

        var saved = new List<BaseItem>();
        var task = new CleanupBackdropsTask(library, NullLogger<CleanupBackdropsTask>.Instance, (item, _) =>
        {
            saved.Add(item);
            return Task.CompletedTask;
        });

        await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);
        return saved;
    }

    [Fact]
    public async Task Execute_RemovesAndDeletesOnlyGeneratedExtras()
    {
        var first = WriteJpeg("fanart.jpg", generated: false);
        var users = WriteJpeg("backdrop1.jpg", generated: false);
        var ours = WriteJpeg("backdrop2.jpg", generated: true);
        var series = new Series
        {
            Id = Guid.NewGuid(),
            Name = "Show",
            ImageInfos = new[] { Backdrop(first), Backdrop(users), Backdrop(ours) }
        };

        var saved = await RunAsync(series);

        Assert.Same(series, Assert.Single(saved));
        Assert.Equal(new[] { first, users }, series.ImageInfos.Select(image => image.Path));
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(users));
        Assert.False(File.Exists(ours));
    }

    /// <summary>
    /// A generated file another item uses as some other image, here an episode's thumb, is removed
    /// from the item with the extra backdrop but stays on disk, since that item still shows it.
    /// </summary>
    [Fact]
    public async Task Execute_KeepsAFileAnotherItemUsesAsADifferentImageType()
    {
        var shared = WriteJpeg("backdrop1.jpg", generated: true);
        var series = new Series
        {
            Id = Guid.NewGuid(),
            Name = "Show",
            ImageInfos = new[] { Backdrop(WriteJpeg("fanart.jpg", generated: false)), Backdrop(shared) }
        };
        var episode = new Episode
        {
            Id = Guid.NewGuid(),
            Name = "Pilot",
            ImageInfos = new[] { new ItemImageInfo { Path = shared, Type = ImageType.Thumb } }
        };

        await RunAsync(series, episode);

        Assert.Single(series.ImageInfos);
        Assert.True(File.Exists(shared));
    }

    [Fact]
    public void Task_HasNoDefaultSchedule()
    {
        var task = new CleanupBackdropsTask(null!, null!);

        Assert.Empty(task.GetDefaultTriggers());
        Assert.Equal("Library", task.Category);
        Assert.Equal("Cleanup Backdrops", task.Name);
    }
}
