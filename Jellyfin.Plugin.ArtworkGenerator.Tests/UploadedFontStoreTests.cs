using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Services.Fonts;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// The fonts folder must take only real fonts Skia can read, keep every file inside the folder, and list and delete what is there.
/// </summary>
public sealed class UploadedFontStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ag-fonts-" + Guid.NewGuid().ToString("N"));

    private string Folder => Path.Combine(_root, "fonts");

    /// <summary>
    /// Deletes the temp folder.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    /// <summary>
    /// Reads a bundled font's bytes, a real font to upload.
    /// </summary>
    internal static byte[] FontBytes(string resource = "Lora.Lora-Bold.ttf")
    {
        using var stream = typeof(BundledFonts).Assembly.GetManifestResourceStream("Jellyfin.Plugin.ArtworkGenerator.Assets.Fonts." + resource)!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>
    /// A real font is stored, and its family and style come from the file, not from its name.
    /// </summary>
    [Fact]
    public void AFontIsStoredWithTheFamilyAndStyleFromTheFile()
    {
        var store = new UploadedFontStore(Folder);

        var result = store.Upload("Bold Titles.ttf", FontBytes());

        Assert.Null(result.Error);
        Assert.NotNull(result.Face);
        Assert.Equal("Lora", result.Face!.Family);
        Assert.Equal("Bold", result.Face.Style);
        Assert.Equal(FontSource.Uploaded, result.Face.Source);
        Assert.True(File.Exists(Path.Combine(Folder, "Bold Titles.ttf")));
    }

    /// <summary>
    /// Only .ttf and .otf files are taken, whatever their contents.
    /// </summary>
    [Theory]
    [InlineData("font.woff")]
    [InlineData("font.txt")]
    [InlineData("font")]
    public void AWrongExtensionIsRefused(string name)
    {
        var result = new UploadedFontStore(Folder).Upload(name, FontBytes());

        Assert.Equal("Only .ttf and .otf fonts can be uploaded.", result.Error);
        Assert.False(Directory.Exists(Folder));
    }

    /// <summary>
    /// A file whose first bytes are not a TrueType or OpenType header is refused, even with a font's extension.
    /// </summary>
    [Fact]
    public void AWrongHeaderIsRefused()
    {
        var result = new UploadedFontStore(Folder).Upload("font.ttf", "<html>not a font</html>"u8.ToArray());

        Assert.Equal("The file is not a TrueType or OpenType font.", result.Error);
    }

    /// <summary>
    /// The three font headers are accepted and anything else is not.
    /// </summary>
    [Theory]
    [InlineData(new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00 }, true)]
    [InlineData(new byte[] { 0x74, 0x72, 0x75, 0x65, 0x00 }, true)]
    [InlineData(new byte[] { 0x4F, 0x54, 0x54, 0x4F, 0x00 }, true)]
    [InlineData(new byte[] { 0x77, 0x4F, 0x46, 0x46, 0x00 }, false)]
    [InlineData(new byte[] { 0x00, 0x01 }, false)]
    [InlineData(new byte[0], false)]
    public void TheHeaderCheckKnowsTheFontHeaders(byte[] data, bool expected)
    {
        Assert.Equal(expected, UploadedFontStore.HasFontHeader(data));
    }

    /// <summary>
    /// A file over the 20 MB cap is refused before Skia reads it.
    /// </summary>
    [Fact]
    public void ATooLargeFontIsRefused()
    {
        var data = new byte[UploadedFontStore.MaxFontBytes + 1];
        data[1] = 0x01;

        var result = new UploadedFontStore(Folder).Upload("big.ttf", data);

        Assert.Equal("The font is larger than 20 MB.", result.Error);
    }

    /// <summary>
    /// An empty upload or a missing name asks for a file.
    /// </summary>
    [Fact]
    public void AnEmptyUploadIsRefused()
    {
        var store = new UploadedFontStore(Folder);

        Assert.Equal("Select a font file.", store.Upload("font.ttf", Array.Empty<byte>()).Error);
        Assert.Equal("Select a font file.", store.Upload(null, FontBytes()).Error);
    }

    /// <summary>
    /// A name that tries to climb out of the folder is cut to its file name, so the font lands inside the folder and nothing is written beside it.
    /// </summary>
    [Theory]
    [InlineData("../../escape.ttf")]
    [InlineData("..\\..\\escape.ttf")]
    [InlineData("/etc/escape.ttf")]
    public void APathTraversalNameStaysInsideTheFolder(string name)
    {
        var result = new UploadedFontStore(Folder).Upload(name, FontBytes());

        Assert.NotNull(result.Face);
        Assert.Equal("escape.ttf", result.Face!.FileName);
        Assert.True(File.Exists(Path.Combine(Folder, "escape.ttf")));
        Assert.Equal(new[] { "fonts" }, Directory.GetFileSystemEntries(_root).Select(Path.GetFileName));
    }

    /// <summary>
    /// A file with a font's header that Skia cannot read is refused and never written.
    /// </summary>
    [Fact]
    public void AnUnloadableFontIsRefused()
    {
        var data = new byte[4096];
        data[1] = 0x01;
        new Random(7).NextBytes(data.AsSpan(4));

        var result = new UploadedFontStore(Folder).Upload("broken.ttf", data);

        Assert.Equal("The font could not be read.", result.Error);
        Assert.False(File.Exists(Path.Combine(Folder, "broken.ttf")));
    }

    /// <summary>
    /// The list holds every font in the folder, including one copied there by hand, and leaves out a file Skia cannot read.
    /// </summary>
    [Fact]
    public void TheListHoldsEveryReadableFontInTheFolder()
    {
        var store = new UploadedFontStore(Folder);
        store.Upload("a.ttf", FontBytes("Lora.Lora-Regular.ttf"));
        File.WriteAllBytes(Path.Combine(Folder, "b.otf"), FontBytes("Anton.Anton-Regular.ttf"));
        File.WriteAllBytes(Path.Combine(Folder, "notes.txt"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(Folder, "junk.ttf"), new byte[] { 0, 1, 0, 0, 9, 9 });
        Directory.SetLastWriteTimeUtc(Folder, DateTime.UtcNow.AddMinutes(1));

        var faces = store.List();

        Assert.Equal(new[] { "Anton", "Lora" }, faces.Select(f => f.Family));
        Assert.Equal(new[] { "b.otf", "a.ttf" }, faces.Select(f => f.FileName));
    }

    /// <summary>
    /// Delete removes the file and its face, and a name not in the folder, or outside it, deletes nothing.
    /// </summary>
    [Fact]
    public void DeleteRemovesOnlyAFontInTheFolder()
    {
        var store = new UploadedFontStore(Folder);
        store.Upload("a.ttf", FontBytes());
        var outside = Path.Combine(_root, "outside.ttf");
        File.WriteAllBytes(outside, FontBytes());

        Assert.False(store.Delete("../outside.ttf"));
        Assert.False(store.Delete("missing.ttf"));
        Assert.False(store.Delete(null));
        Assert.True(store.Delete("a.ttf"));

        Assert.Empty(store.List());
        Assert.False(File.Exists(Path.Combine(Folder, "a.ttf")));
        Assert.True(File.Exists(outside));
    }

    /// <summary>
    /// Uploading a file of the same name replaces it, and the list shows the new font.
    /// </summary>
    [Fact]
    public void UploadingTheSameNameReplacesTheFont()
    {
        var store = new UploadedFontStore(Folder);
        store.Upload("title.ttf", FontBytes("Lora.Lora-Regular.ttf"));

        store.Upload("title.ttf", FontBytes("Anton.Anton-Regular.ttf"));

        Assert.Equal("Anton", Assert.Single(store.List()).Family);
    }
}
