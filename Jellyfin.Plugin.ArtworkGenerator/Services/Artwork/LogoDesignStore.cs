using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Artwork
{
    /// <summary>
    /// The stored shape of the logo design file.
    /// </summary>
    public class LogoDesignDocument
    {
        /// <summary>Gets or sets the stored logo designs.</summary>
        [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "List<T> is the shape the JSON store round-trips")]
        [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Setter required for JSON deserialization")]
        public List<LogoConfiguration> Logos { get; set; } = new List<LogoConfiguration>();
    }

    /// <summary>
    /// Keeps the logo designs in their own JSON file beside the plugin's configuration instead of
    /// inside it, so editing a logo never rewrites the whole configuration and the designs can grow
    /// without bloating the XML the server reads on every start.
    /// </summary>
    /// <remarks>
    /// Backed by the shared <see cref="JsonFileStore{T}"/>, which tolerates a missing or corrupt
    /// file and writes atomically. The parameterless constructor keeps everything in memory, for
    /// tests and for any caller with no data directory to write to.
    /// </remarks>
    public sealed class LogoDesignStore
    {
        private readonly JsonFileStore<LogoDesignDocument>? _file;
        private List<LogoConfiguration> _memory = new List<LogoConfiguration>();

        /// <summary>
        /// Initializes a new instance of the <see cref="LogoDesignStore"/> class backed by a file.
        /// </summary>
        /// <param name="path">Absolute path to the JSON file holding the designs.</param>
        public LogoDesignStore(string path)
        {
            _file = new JsonFileStore<LogoDesignDocument>(path);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LogoDesignStore"/> class that keeps the
        /// designs in memory only.
        /// </summary>
        public LogoDesignStore()
        {
        }

        /// <summary>Returns the stored designs, or an empty list when there are none.</summary>
        public IReadOnlyList<LogoConfiguration> Load()
        {
            return _file == null ? _memory.ToList() : _file.Load().Logos;
        }

        /// <summary>Replaces the stored designs.</summary>
        /// <param name="logos">The designs to store.</param>
        public void Save(IEnumerable<LogoConfiguration> logos)
        {
            ArgumentNullException.ThrowIfNull(logos);

            var list = logos.ToList();
            if (_file == null)
            {
                _memory = list;
                return;
            }

            _file.Save(new LogoDesignDocument { Logos = list });
        }
    }
}
