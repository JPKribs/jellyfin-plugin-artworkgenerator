using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    /// <summary>
    /// A rendered poster, and optionally the cropped canvas it was drawn over, as encoded
    /// JPEG bytes. Posters are handed straight to Jellyfin's image pipeline, so they never
    /// touch disk on the way.
    /// </summary>
    public class PosterGenerationResult
    {
        public PosterGenerationResult(byte[] poster, byte[]? backdrop, int width, int height)
        {
            Poster = poster;
            Backdrop = backdrop;
            Width = width;
            Height = height;
        }

        /// <summary>
        /// Gets the poster width in pixels, surfaced to Jellyfin's image picker.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the poster height in pixels, surfaced to Jellyfin's image picker.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Gets the encoded poster image. Always populated.
        /// </summary>
        [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Encoded image payload passed straight to Jellyfin")]
        public byte[] Poster { get; }

        /// <summary>
        /// Gets the encoded backdrop image, or null when backdrop generation is disabled
        /// or the canvas was not an extracted frame.
        /// </summary>
        [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Encoded image payload passed straight to Jellyfin")]
        public byte[]? Backdrop { get; }
    }
}
