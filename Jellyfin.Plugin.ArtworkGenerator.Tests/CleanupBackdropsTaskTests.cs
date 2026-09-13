using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Tests for choosing which backdrops the Cleanup Backdrops task removes.
/// </summary>
public class CleanupBackdropsTaskTests
{
    private static ItemImageInfo Backdrop(string path) => new() { Path = path, Type = ImageType.Backdrop };

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

    [Fact]
    public void IsSafeToDelete_SkipsAFileTheKeptBackdropStillUses()
    {
        var kept = Backdrop("/m/backdrop.jpg");

        Assert.False(CleanupBackdropsTask.IsSafeToDelete(Backdrop("/m/backdrop.jpg"), new[] { kept }));
        Assert.True(CleanupBackdropsTask.IsSafeToDelete(Backdrop("/m/backdrop1.jpg"), new[] { kept }));
    }

    [Fact]
    public void IsSafeToDelete_SkipsABackdropKnownOnlyByWebAddress()
    {
        Assert.False(CleanupBackdropsTask.IsSafeToDelete(Backdrop("https://example.org/backdrop.jpg"), Enumerable.Empty<ItemImageInfo>()));
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
