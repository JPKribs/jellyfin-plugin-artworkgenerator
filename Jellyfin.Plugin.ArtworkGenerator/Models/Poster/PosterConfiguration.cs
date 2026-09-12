using System;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    public class PosterConfiguration
    {
        public Guid Id { get; set; }

        public string Name { get; set; }

        public PosterSettings Settings { get; set; }

        public bool IsDefault { get; set; }

        public PosterConfiguration()
        {
            Id = Guid.NewGuid();
            Name = "Poster";
            IsDefault = false;
            Settings = new PosterSettings();
        }
    }
}
