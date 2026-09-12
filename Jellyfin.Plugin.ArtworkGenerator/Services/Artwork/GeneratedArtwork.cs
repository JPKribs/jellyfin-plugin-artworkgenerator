using System.Diagnostics.CodeAnalysis;
using MediaBrowser.Model.Drawing;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Artwork
{
    /// <summary>
    /// One encoded image produced for an item.
    /// </summary>
    public sealed class GeneratedArtwork
    {
        public GeneratedArtwork(byte[] bytes, string mimeType, ImageFormat format, int width, int height)
        {
            Bytes = bytes;
            MimeType = mimeType;
            Format = format;
            Width = width;
            Height = height;
        }

        [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Encoded image payload passed straight to Jellyfin")]
        public byte[] Bytes { get; }

        public string MimeType { get; }

        public ImageFormat Format { get; }

        public int Width { get; }

        public int Height { get; }
    }
}
