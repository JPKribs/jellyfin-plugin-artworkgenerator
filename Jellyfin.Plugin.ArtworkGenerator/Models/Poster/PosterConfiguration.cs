using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    public class PosterConfiguration
    {
        public Guid Id { get; set; }

        public string Name { get; set; }

        public PosterSettings Settings { get; set; }

        [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "List<T> required for XML serialization")]
        [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Required for XML serialization")]
        public List<Guid> SeriesIds { get; set; }

        public bool IsDefault { get; set; }

        public PosterConfiguration()
        {
            Id = Guid.NewGuid();
            Name = "Poster";
            IsDefault = false;
            Settings = new PosterSettings();
            SeriesIds = new List<Guid>();
        }
    }
}
