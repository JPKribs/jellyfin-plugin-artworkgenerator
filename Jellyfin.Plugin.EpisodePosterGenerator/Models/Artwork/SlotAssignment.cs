using System;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Models
{
    /// <summary>
    /// One cell of a profile: whether an item kind gets a given image, and which design draws it.
    /// </summary>
    public class SlotAssignment
    {
        /// <summary>Gets or sets the item kind this cell applies to.</summary>
        public ArtworkItemKind Kind { get; set; }

        /// <summary>Gets or sets the image slot this cell fills.</summary>
        public ArtworkSlot Slot { get; set; }

        /// <summary>Gets or sets a value indicating whether the plugin generates this image.</summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Gets or sets the design that draws the image: a poster design for Primary and Thumb, a
        /// logo design for Logo. Ignored for Backdrop, which is a plain extraction.
        /// </summary>
        public Guid DesignId { get; set; }
    }
}
