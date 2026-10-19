using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Tests for the tag that marks a backdrop as the plugin's own, which Cleanup Backdrops relies on
/// to leave everyone else's artwork alone.
/// </summary>
public class GeneratedImageMarkerTests
{
    private static byte[] Jpeg()
    {
        using var bitmap = new SKBitmap(40, 20);
        bitmap.Erase(SKColors.OrangeRed);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    [Fact]
    public void Tag_MarksAJpegThatStillDecodes()
    {
        var plain = Jpeg();
        var tagged = GeneratedImageMarker.Tag(plain);

        Assert.False(GeneratedImageMarker.IsTagged(plain));
        Assert.True(GeneratedImageMarker.IsTagged(tagged));

        using var decoded = SKBitmap.Decode(tagged);
        Assert.NotNull(decoded);
        Assert.Equal(40, decoded.Width);
        Assert.Equal(20, decoded.Height);
    }

    /// <summary>
    /// JFIF expects its header straight after the start of image, so the comment follows it.
    /// </summary>
    [Fact]
    public void Tag_KeepsTheJfifHeaderFirst()
    {
        var plain = Jpeg();
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, plain[..4]);

        var tagged = GeneratedImageMarker.Tag(plain);
        var app0End = 4 + ((plain[4] << 8) | plain[5]);

        Assert.Equal(plain[..app0End], tagged[..app0End]);
        Assert.Equal(new byte[] { 0xFF, 0xFE }, tagged[app0End..(app0End + 2)]);
        Assert.Equal(plain[app0End..], tagged[(tagged.Length - (plain.Length - app0End))..]);
    }

    /// <summary>A comment placed straight after the start of image is still recognized.</summary>
    [Fact]
    public void IsTagged_FindsTheCommentBeforeTheJfifHeaderToo()
    {
        var plain = Jpeg();
        var comment = System.Text.Encoding.ASCII.GetBytes("Jellyfin Artwork Generator");
        var segment = new byte[] { 0xFF, 0xFE, 0x00, (byte)(comment.Length + 2) };
        var early = plain[..2].Concat(segment).Concat(comment).Concat(plain[2..]).ToArray();

        Assert.True(GeneratedImageMarker.IsTagged(early));
    }

    [Fact]
    public void Tag_IsAppliedOnlyOnce()
    {
        var once = GeneratedImageMarker.Tag(Jpeg());

        Assert.Same(once, GeneratedImageMarker.Tag(once));
    }

    [Fact]
    public void Tag_LeavesAnythingButAJpegUnchanged()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        Assert.Same(png, GeneratedImageMarker.Tag(png));
        Assert.False(GeneratedImageMarker.IsTagged(png));
    }

    [Fact]
    public void IsTaggedFile_ReadsTheFileOnDisk()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ag-marker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var tagged = Path.Combine(folder, "tagged.jpg");
            var plain = Path.Combine(folder, "plain.jpg");
            var empty = Path.Combine(folder, "empty.jpg");
            File.WriteAllBytes(tagged, GeneratedImageMarker.Tag(Jpeg()));
            File.WriteAllBytes(plain, Jpeg());
            File.WriteAllBytes(empty, Array.Empty<byte>());

            Assert.True(GeneratedImageMarker.IsTaggedFile(tagged));
            Assert.False(GeneratedImageMarker.IsTaggedFile(plain));
            Assert.False(GeneratedImageMarker.IsTaggedFile(empty));
            Assert.False(GeneratedImageMarker.IsTaggedFile(Path.Combine(folder, "missing.jpg")));
            Assert.False(GeneratedImageMarker.IsTaggedFile(string.Empty));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
