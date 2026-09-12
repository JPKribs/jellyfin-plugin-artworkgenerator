using System;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    /// <summary>
    /// A named logo design that profiles can point their Logo slot at.
    /// </summary>
    public class LogoConfiguration
    {
        public LogoConfiguration()
        {
            Id = Guid.NewGuid();
            Name = "Logo";
            Settings = new LogoSettings();
        }

        public Guid Id { get; set; }

        public string Name { get; set; }

        public LogoSettings Settings { get; set; }
    }
}
