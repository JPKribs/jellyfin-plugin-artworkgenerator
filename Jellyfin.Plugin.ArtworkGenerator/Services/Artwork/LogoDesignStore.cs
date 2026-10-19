using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
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
    /// Written through the shared <see cref="JsonFileStore{T}"/>, which writes atomically. Reading
    /// is done here rather than through it, because the store treats a corrupt file the same as a
    /// missing one, and only a missing file may be seeded with a fresh design. The parameterless
    /// constructor keeps everything in memory, for tests and for any caller with no data directory
    /// to write to.
    /// </remarks>
    public sealed class LogoDesignStore
    {
        private readonly string? _path;
        private readonly JsonFileStore<LogoDesignDocument>? _file;
        private List<LogoConfiguration> _memory = new List<LogoConfiguration>();

        /// <summary>
        /// Initializes a new instance of the <see cref="LogoDesignStore"/> class backed by a file.
        /// </summary>
        /// <param name="path">Absolute path to the JSON file holding the designs.</param>
        public LogoDesignStore(string path)
        {
            _file = new JsonFileStore<LogoDesignDocument>(path);
            _path = path;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LogoDesignStore"/> class that keeps the
        /// designs in memory only.
        /// </summary>
        public LogoDesignStore()
        {
        }

        /// <summary>Returns the stored designs, or an empty list when there are none or the file cannot be read.</summary>
        public IReadOnlyList<LogoConfiguration> Load()
        {
            return TryLoad(out var logos) ? logos : Array.Empty<LogoConfiguration>();
        }

        /// <summary>
        /// Reads the stored designs. Returns false when the file exists but cannot be read, so the
        /// caller can tell a damaged file, which must be left alone, from a first run with no file.
        /// </summary>
        /// <param name="logos">The stored designs; empty when there are none or the file cannot be read.</param>
        public bool TryLoad(out IReadOnlyList<LogoConfiguration> logos)
        {
            logos = Array.Empty<LogoConfiguration>();
            if (_path == null)
            {
                logos = _memory.ToList();
                return true;
            }

            try
            {
                if (!File.Exists(_path))
                {
                    return true;
                }

                var document = JsonSerializer.Deserialize<LogoDesignDocument>(File.ReadAllText(_path));
                if (document?.Logos == null)
                {
                    return false;
                }

                logos = document.Logos.Where(logo => logo != null).ToList();
                return true;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Copies an unreadable file aside to a <c>.bak</c> beside it, so a later save from the
        /// configuration page cannot destroy the only copy of the user's designs. Returns the backup
        /// path, or null when there was nothing to copy or the copy failed.
        /// </summary>
        public string? PreserveUnreadable()
        {
            if (_path == null || !File.Exists(_path))
            {
                return null;
            }

            var backup = _path + ".bak";
            try
            {
                File.Copy(_path, backup, overwrite: true);
                return backup;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
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
