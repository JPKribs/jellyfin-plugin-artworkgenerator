using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    /// <summary>
    /// A complete look for a set of series: which image each item kind gets, and which design draws it.
    /// </summary>
    /// <remarks>
    /// Designs say how an image looks; a profile says where each design is used. Series are assigned
    /// to profiles, so a series gets one consistent set of posters, thumbs, logos, and backdrops. The
    /// default profile covers every series not assigned elsewhere.
    /// </remarks>
    public class ArtworkProfile
    {
        private static readonly (ArtworkItemKind Kind, ArtworkSlot Slot)[] Supported =
        {
            (ArtworkItemKind.Series, ArtworkSlot.Primary),
            (ArtworkItemKind.Series, ArtworkSlot.Thumb),
            (ArtworkItemKind.Series, ArtworkSlot.Logo),
            (ArtworkItemKind.Series, ArtworkSlot.Backdrop),
            (ArtworkItemKind.Season, ArtworkSlot.Primary),
            (ArtworkItemKind.Season, ArtworkSlot.Thumb),
            (ArtworkItemKind.Season, ArtworkSlot.Backdrop),
            (ArtworkItemKind.Episode, ArtworkSlot.Primary),
            (ArtworkItemKind.Episode, ArtworkSlot.Thumb),
            (ArtworkItemKind.Episode, ArtworkSlot.Backdrop),
            (ArtworkItemKind.Movie, ArtworkSlot.Primary),
            (ArtworkItemKind.Movie, ArtworkSlot.Thumb),
            (ArtworkItemKind.Movie, ArtworkSlot.Logo),
            (ArtworkItemKind.Movie, ArtworkSlot.Backdrop)
        };

        public ArtworkProfile()
        {
            Id = Guid.NewGuid();
            Name = "Profile";
            SeriesIds = new List<Guid>();
            MovieIds = new List<Guid>();
            Slots = new List<SlotAssignment>();
            Backdrop = new BackdropSettings();
        }

        /// <summary>
        /// Gets every item kind and slot pairing the plugin can fill. Jellyfin clients do not show
        /// logos for seasons or episodes, so those are not offered.
        /// </summary>
        public static IReadOnlyList<(ArtworkItemKind Kind, ArtworkSlot Slot)> SupportedSlots => Supported;

        public Guid Id { get; set; }

        public string Name { get; set; }

        public bool IsDefault { get; set; }

        [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "List<T> required for XML serialization")]
        [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Setter required for XML serialization")]
        public List<Guid> SeriesIds { get; set; }

        /// <summary>
        /// Gets or sets the films assigned to this profile. Only consulted when the scope includes
        /// movies.
        /// </summary>
        [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "List<T> required for XML serialization")]
        [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Setter required for XML serialization")]
        public List<Guid> MovieIds { get; set; }

        /// <summary>
        /// Gets or sets what this profile applies to. Defaults to TV, so a profile written before
        /// movies were supported keeps doing exactly what it did.
        /// </summary>
        public ProfileScope Scope { get; set; } = ProfileScope.Tv;

        /// <summary>Gets or sets the shape of series primary images.</summary>
        public ArtworkShape SeriesPrimaryShape { get; set; } = ArtworkShape.Portrait;

        /// <summary>Gets or sets the shape of season primary images.</summary>
        public ArtworkShape SeasonPrimaryShape { get; set; } = ArtworkShape.Portrait;

        /// <summary>Gets or sets the shape of episode primary images.</summary>
        public ArtworkShape EpisodePrimaryShape { get; set; } = ArtworkShape.Landscape;

        /// <summary>Gets or sets the shape of movie primary images.</summary>
        public ArtworkShape MoviePrimaryShape { get; set; } = ArtworkShape.Portrait;

        [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "List<T> required for XML serialization")]
        [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Setter required for XML serialization")]
        public List<SlotAssignment> Slots { get; set; }

        public BackdropSettings Backdrop { get; set; }

        /// <summary>
        /// Returns true when the plugin offers the given image for the given item kind.
        /// </summary>
        public static bool IsSupported(ArtworkItemKind kind, ArtworkSlot slot)
        {
            return Array.IndexOf(Supported, (kind, slot)) >= 0;
        }

        /// <summary>
        /// Returns the cell for an item kind and slot, or null when the profile has none.
        /// </summary>
        public SlotAssignment? GetSlot(ArtworkItemKind kind, ArtworkSlot slot)
        {
            return Slots.FirstOrDefault(s => s.Kind == kind && s.Slot == slot);
        }

        /// <summary>
        /// Returns the primary image shape for an item kind.
        /// </summary>
        public ArtworkShape GetPrimaryShape(ArtworkItemKind kind) => kind switch
        {
            ArtworkItemKind.Series => SeriesPrimaryShape,
            ArtworkItemKind.Season => SeasonPrimaryShape,
            ArtworkItemKind.Movie => MoviePrimaryShape,
            _ => EpisodePrimaryShape
        };

        /// <summary>
        /// Returns true when this profile covers the given kind of item. A profile scoped to TV
        /// never claims a film, and one scoped to movies never claims a series.
        /// </summary>
        public bool AppliesTo(ArtworkItemKind kind)
        {
            return Scope switch
            {
                ProfileScope.Both => true,
                ProfileScope.Movies => kind == ArtworkItemKind.Movie,
                _ => kind != ArtworkItemKind.Movie
            };
        }

        /// <summary>
        /// Returns the shape an image slot renders at. Thumbs and backdrops are always landscape.
        /// </summary>
        public ArtworkShape GetShape(ArtworkItemKind kind, ArtworkSlot slot)
        {
            return slot == ArtworkSlot.Primary ? GetPrimaryShape(kind) : ArtworkShape.Landscape;
        }
    }
}
