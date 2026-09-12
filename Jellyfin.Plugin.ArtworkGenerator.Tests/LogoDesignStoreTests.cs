using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services.Artwork;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Tests for the logo designs' own file, which keeps them out of the plugin configuration.
/// </summary>
public class LogoDesignStoreTests
{
    [Fact]
    public void MissingFile_LoadsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), "ag-logos-" + Guid.NewGuid().ToString("N") + ".json");

        Assert.Empty(new LogoDesignStore(path).Load());
    }

    [Fact]
    public void SaveThenLoad_RoundTripsTheDesigns()
    {
        var path = Path.Combine(Path.GetTempPath(), "ag-logos-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            var design = new LogoConfiguration
            {
                Name = "Outlined",
                Settings = new LogoSettings { OutlineEnabled = true, SubtitleMode = LogoSubtitleMode.SubtitleLarge, Fill = LogoFill.Photo }
            };

            new LogoDesignStore(path).Save(new[] { design });
            var loaded = new LogoDesignStore(path).Load();

            var reloaded = Assert.Single(loaded);
            Assert.Equal(design.Id, reloaded.Id);
            Assert.Equal("Outlined", reloaded.Name);
            Assert.True(reloaded.Settings.OutlineEnabled);
            Assert.Equal(LogoSubtitleMode.SubtitleLarge, reloaded.Settings.SubtitleMode);
            Assert.Equal(LogoFill.Photo, reloaded.Settings.Fill);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CorruptFile_LoadsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), "ag-logos-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{ not json at all");

        try
        {
            Assert.Empty(new LogoDesignStore(path).Load());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MemoryStore_KeepsDesignsWithoutAFile()
    {
        var store = new LogoDesignStore();
        store.Save(new[] { new LogoConfiguration { Name = "Kept" } });

        Assert.Equal("Kept", store.Load().Single().Name);
    }
}
