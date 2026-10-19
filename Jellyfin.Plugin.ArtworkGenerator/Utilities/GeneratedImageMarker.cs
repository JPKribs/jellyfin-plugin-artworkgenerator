using System;
using System.IO;
using System.Text;

namespace Jellyfin.Plugin.ArtworkGenerator.Utilities;

/// <summary>
/// Tags a generated JPEG with a comment segment naming the plugin, so a file the plugin produced
/// can be told apart on disk from artwork the user or another provider put there.
/// </summary>
/// <remarks>
/// Jellyfin writes the bytes a provider hands it to disk unchanged, whether from a refresh or a
/// pick in Edit Images, so the tag survives into the saved file. It goes after the JFIF header
/// when there is one, since JFIF expects its header first, and every decoder skips a comment.
/// </remarks>
public static class GeneratedImageMarker
{
    private static readonly byte[] Comment = Encoding.ASCII.GetBytes("Jellyfin Artwork Generator");

    // How much of a file is read to find the tag. It sits among the first few header segments,
    // which in a JPEG the plugin rendered take a few dozen bytes.
    private const int HeadLength = 4096;

    // Tag
    // Returns the JPEG with the comment inserted after its start of image marker and any JFIF
    // header, or the bytes unchanged when they are not a JPEG or are already tagged.
    public static byte[] Tag(byte[] jpeg)
    {
        ArgumentNullException.ThrowIfNull(jpeg);

        if (jpeg.Length < 2 || jpeg[0] != 0xFF || jpeg[1] != 0xD8 || IsTagged(jpeg))
        {
            return jpeg;
        }

        var at = 2;
        if (jpeg.Length >= 6 && jpeg[2] == 0xFF && jpeg[3] == 0xE0)
        {
            var app0End = 4 + ((jpeg[4] << 8) | jpeg[5]);
            if (app0End <= jpeg.Length)
            {
                at = app0End;
            }
        }

        var segmentLength = Comment.Length + 2;
        var tagged = new byte[jpeg.Length + 2 + segmentLength];
        Array.Copy(jpeg, 0, tagged, 0, at);
        tagged[at] = 0xFF;
        tagged[at + 1] = 0xFE;
        tagged[at + 2] = (byte)(segmentLength >> 8);
        tagged[at + 3] = (byte)(segmentLength & 0xFF);
        Comment.CopyTo(tagged, at + 4);
        Array.Copy(jpeg, at, tagged, at + 4 + Comment.Length, jpeg.Length - at);

        return tagged;
    }

    // IsTagged
    // True when the header segments at the start of the bytes hold the comment Tag writes.
    public static bool IsTagged(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
        {
            return false;
        }

        // Walk the application and comment segments that open the file; anything else means the
        // image data has begun and the tag is not there.
        var at = 2;
        while (at + 4 <= bytes.Length && bytes[at] == 0xFF)
        {
            var marker = bytes[at + 1];
            if (marker != 0xFE && (marker < 0xE0 || marker > 0xEF))
            {
                return false;
            }

            var length = (bytes[at + 2] << 8) | bytes[at + 3];
            if (length < 2)
            {
                return false;
            }

            if (marker == 0xFE
                && length == Comment.Length + 2
                && at + 4 + Comment.Length <= bytes.Length
                && bytes.Slice(at + 4, Comment.Length).SequenceEqual(Comment))
            {
                return true;
            }

            at += 2 + length;
        }

        return false;
    }

    // IsTaggedFile
    // True when the file on disk carries the tag. A file that is missing or cannot be read is not.
    public static bool IsTaggedFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var head = new byte[HeadLength];
            var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            return IsTagged(head.AsSpan(0, read));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
