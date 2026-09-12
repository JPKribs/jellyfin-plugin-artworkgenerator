using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Configuration;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services.Artwork;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtworkGenerator.Services
{
    /// <summary>
    /// Resolves which profile, poster design, and logo design apply to an item, and brings older
    /// configurations up to the profile model.
    /// </summary>
    public partial class PosterConfigurationService
    {
        /// <summary>Id of the separate portrait design an earlier build synthesized. Every design now renders both shapes, so it is retired on load.</summary>
        public static readonly Guid DefaultPortraitDesignId = new("6f1c2a4e-3b7d-4e21-9a55-0c8d7e1f2a01");

        /// <summary>Id of the logo design synthesized for configurations that have none.</summary>
        public static readonly Guid DefaultLogoDesignId = new("6f1c2a4e-3b7d-4e21-9a55-0c8d7e1f2a02");

        /// <summary>Id of the default profile synthesized for configurations that have none.</summary>
        public static readonly Guid DefaultProfileId = new("6f1c2a4e-3b7d-4e21-9a55-0c8d7e1f2a03");

        /// <summary>The name every synthesized default carries: design, profile, and logo design.</summary>
        private const string DefaultName = "Default";

        private readonly ILogger<PosterConfigurationService> _logger;
        private readonly LogoDesignStore _logoStore;
        private volatile Snapshot _snapshot = Snapshot.Empty;

        public PosterConfigurationService(ILogger<PosterConfigurationService> logger, LogoDesignStore? logoStore = null)
        {
            _logger = logger;
            _logoStore = logoStore ?? new LogoDesignStore();
        }

        /// <summary>Gets the default landscape design.</summary>
        public PosterSettings DefaultDesign => _snapshot.DefaultDesign;

        // Initialize
        // Builds the lookups from the plugin configuration, first filling in anything an older
        // configuration lacks: a logo design and profiles.
        //
        // Deliberately never writes the configuration back. This runs from the plugin constructor,
        // before anything has established that the file on disk loaded correctly, and saving here
        // would turn a recoverable load failure into permanent loss of the user's settings. Anything
        // synthesized lives in memory only, is rebuilt identically on the next start (the synthesized
        // items use fixed ids), and is persisted by the user's next save in the configuration page.
        public void Initialize(PluginConfiguration config)
        {
            ArgumentNullException.ThrowIfNull(config);

            var design = EnsureDefaultDesign(config);

            // TODO (12.0.2.1): delete this call and PosterConfigurationService.Migration.cs.
            MigrateConfiguration(config, design);

            var logoDesigns = LoadLogoDesigns();
            var logo = logoDesigns[0];

            // TODO (12.0.2.1): delete this call and PosterConfigurationService.Migration.cs.
            MigrateLegacyDesignsToProfiles(config, design, logo);

            EnsureProfiles(config, design, logo);
            foreach (var profile in config.Profiles)
            {
                EnsureSlots(profile, design, logo);
            }

            var designs = new Dictionary<Guid, PosterSettings>();
            foreach (var poster in config.PosterConfigurations)
            {
                designs.TryAdd(poster.Id, poster.Settings);
            }

            var logos = new Dictionary<Guid, LogoSettings>();
            foreach (var logoDesign in logoDesigns)
            {
                logos.TryAdd(logoDesign.Id, logoDesign.Settings);
            }

            var bySeries = new Dictionary<Guid, ArtworkProfile>();
            var byMovie = new Dictionary<Guid, ArtworkProfile>();
            var duplicates = 0;
            foreach (var profile in config.Profiles.Where(p => !p.IsDefault))
            {
                foreach (var seriesId in profile.SeriesIds)
                {
                    if (!bySeries.TryAdd(seriesId, profile))
                    {
                        duplicates++;
                        _logger.LogWarning("Series {SeriesId} is assigned to more than one profile; using the first", seriesId);
                    }
                }

                // A film and a standalone video are both assigned by their own id, so they share
                // one index rather than each having a near-identical copy.
                foreach (var ownId in (profile.MovieIds ?? new List<Guid>()).Concat(profile.VideoIds ?? new List<Guid>()))
                {
                    if (!byMovie.TryAdd(ownId, profile))
                    {
                        duplicates++;
                        _logger.LogWarning("Item {ItemId} is assigned to more than one profile; using the first", ownId);
                    }
                }
            }

            // Atomic swap: readers see either the old or the new state, never a partial one.
            _snapshot = new Snapshot(
                designs,
                logos,
                design.Settings,
                logoDesigns,
                logo.Settings,
                config.Profiles.First(p => p.IsDefault),
                bySeries,
                byMovie);

            _logger.LogInformation(
                "Artwork configuration loaded: {Designs} design(s), {Logos} logo design(s), {Profiles} profile(s), {Assigned} assigned series, {Duplicates} duplicate assignment(s) ignored",
                designs.Count,
                logos.Count,
                config.Profiles.Count,
                bySeries.Count,
                duplicates);
        }

        /// <summary>
        /// Returns the profile for an item: the one it is assigned to, or the default. Every profile
        /// covers every kind, so an item always has a profile.
        /// </summary>
        /// <param name="kind">The kind of item being drawn.</param>
        /// <param name="id">The series id for a TV item, or the item's own id otherwise.</param>
        public ArtworkProfile GetProfileFor(ArtworkItemKind kind, Guid id)
        {
            var snapshot = _snapshot;
            var index = kind.IsStandalone() ? snapshot.ByOwnId : snapshot.BySeries;

            return id != Guid.Empty && index.TryGetValue(id, out var assigned)
                ? assigned
                : snapshot.DefaultProfile;
        }

        /// <summary>
        /// Returns the poster design a slot points at, or the default design when the design has
        /// been deleted.
        /// </summary>
        public PosterSettings GetDesignForSlot(SlotAssignment assignment)
        {
            var snapshot = _snapshot;
            return assignment != null && snapshot.Designs.TryGetValue(assignment.DesignId, out var design)
                ? design
                : snapshot.DefaultDesign;
        }

        /// <summary>
        /// Returns the logo design a slot points at, or the default logo design.
        /// </summary>
        public LogoSettings GetLogoForSlot(SlotAssignment assignment)
        {
            var snapshot = _snapshot;
            return assignment != null && snapshot.Logos.TryGetValue(assignment.DesignId, out var logo)
                ? logo
                : snapshot.DefaultLogo;
        }

        // EnsureDefaultDesign
        // The one default landscape design, created in memory when missing.
        private PosterConfiguration EnsureDefaultDesign(PluginConfiguration config)
        {
            var defaults = config.PosterConfigurations.Where(c => c.IsDefault).ToList();

            if (defaults.Count == 0)
            {
                _logger.LogInformation("No default design found, creating one in memory");
                var created = new PosterConfiguration { Name = DefaultName, IsDefault = true };
                config.PosterConfigurations.Insert(0, created);
                return created;
            }

            if (defaults.Count > 1)
            {
                _logger.LogWarning("Multiple default designs found, using the first");
            }

            return defaults[0];
        }

        // LoadLogoDesigns
        // Logo designs live in their own file beside the configuration, so a logo edit never
        // rewrites the whole configuration. A first run has no file and gets one design.
        private List<LogoConfiguration> LoadLogoDesigns()
        {
            var logos = _logoStore.Load().ToList();
            var changed = false;

            if (logos.Count == 0)
            {
                logos.Add(new LogoConfiguration { Id = DefaultLogoDesignId, Name = DefaultName });
                changed = true;
            }

            foreach (var logo in logos)
            {
                logo.Settings ??= new LogoSettings();
            }

            if (changed)
            {
                _logoStore.Save(logos);
            }

            return logos;
        }

        /// <summary>Returns the logo designs.</summary>
        public IReadOnlyList<LogoConfiguration> GetLogoDesigns() => _snapshot.LogoDesigns;

        /// <summary>
        /// Replaces the logo designs, persisting them to their own file and refreshing the lookups.
        /// </summary>
        /// <param name="config">The plugin configuration, reread to rebuild the lookups.</param>
        /// <param name="logos">The designs to store. At least one is required.</param>
        public void SaveLogoDesigns(PluginConfiguration config, IReadOnlyList<LogoConfiguration> logos)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(logos);

            if (logos.Count == 0)
            {
                throw new ArgumentException("At least one logo design is required.", nameof(logos));
            }

            var stored = new List<LogoConfiguration>(logos.Count);
            foreach (var logo in logos)
            {
                logo.Id = logo.Id == Guid.Empty ? Guid.NewGuid() : logo.Id;
                logo.Settings ??= new LogoSettings();
                logo.Name = string.IsNullOrWhiteSpace(logo.Name) ? DefaultName : logo.Name;
                stored.Add(logo);
            }

            _logoStore.Save(stored);
            _logger.LogInformation("Saved {Count} logo design(s)", stored.Count);

            Initialize(config);
        }

        // EnsureProfiles
        // Gives a configuration with no profiles the one default profile everything falls back to,
        // and makes sure exactly one profile is marked default.
        private void EnsureProfiles(PluginConfiguration config, PosterConfiguration design, LogoConfiguration logo)
        {
            if (config.Profiles.Count == 0)
            {
                config.Profiles.Add(CreateProfile(DefaultProfileId, DefaultName, true, design, design, logo));
                _logger.LogInformation("Created the default profile");
            }

            var defaults = config.Profiles.Where(p => p.IsDefault).ToList();
            if (defaults.Count == 0)
            {
                config.Profiles[0].IsDefault = true;
            }
            else
            {
                foreach (var extra in defaults.Skip(1))
                {
                    extra.IsDefault = false;
                }
            }
        }

        internal static ArtworkProfile CreateProfile(
            Guid id,
            string name,
            bool isDefault,
            PosterConfiguration episodeDesign,
            PosterConfiguration design,
            LogoConfiguration logo)
        {
            var source = episodeDesign.Settings;
            var profile = new ArtworkProfile
            {
                Id = id,
                Name = name,
                IsDefault = isDefault,
                Backdrop = new BackdropSettings()
            };

            Add(profile, ArtworkItemKind.Series, ArtworkSlot.Primary, true, design.Id);
            Add(profile, ArtworkItemKind.Series, ArtworkSlot.Thumb, true, design.Id);
            Add(profile, ArtworkItemKind.Series, ArtworkSlot.Logo, true, logo.Id);
            Add(profile, ArtworkItemKind.Series, ArtworkSlot.Backdrop, true, Guid.Empty);
            Add(profile, ArtworkItemKind.Season, ArtworkSlot.Primary, true, design.Id);
            Add(profile, ArtworkItemKind.Season, ArtworkSlot.Thumb, true, design.Id);
            Add(profile, ArtworkItemKind.Season, ArtworkSlot.Backdrop, false, Guid.Empty);
            Add(profile, ArtworkItemKind.Episode, ArtworkSlot.Primary, true, episodeDesign.Id);
            Add(profile, ArtworkItemKind.Episode, ArtworkSlot.Backdrop, source.GenerateBackdrop, Guid.Empty);

            return profile;
        }

        // EnsureSlots
        // Adds any cell a profile is missing, switched off, so a profile saved by an older version
        // gains new slots without silently starting to generate them.
        private static void EnsureSlots(ArtworkProfile profile, PosterConfiguration design, LogoConfiguration logo)
        {
            profile.Backdrop ??= new BackdropSettings();

            foreach (var (kind, slot) in ArtworkProfile.SupportedSlots)
            {
                if (profile.GetSlot(kind, slot) != null)
                {
                    continue;
                }

                var designId = slot switch
                {
                    ArtworkSlot.Logo => logo.Id,
                    ArtworkSlot.Backdrop => Guid.Empty,
                    _ => design.Id
                };

                Add(profile, kind, slot, false, designId);
            }
        }

        internal static void Add(ArtworkProfile profile, ArtworkItemKind kind, ArtworkSlot slot, bool enabled, Guid designId)
        {
            profile.Slots.Add(new SlotAssignment { Kind = kind, Slot = slot, Enabled = enabled, DesignId = designId });
        }

        private sealed record Snapshot(
            IReadOnlyDictionary<Guid, PosterSettings> Designs,
            IReadOnlyDictionary<Guid, LogoSettings> Logos,
            PosterSettings DefaultDesign,
            IReadOnlyList<LogoConfiguration> LogoDesigns,
            LogoSettings DefaultLogo,
            ArtworkProfile DefaultProfile,
            IReadOnlyDictionary<Guid, ArtworkProfile> BySeries,
            IReadOnlyDictionary<Guid, ArtworkProfile> ByOwnId)
        {
            public static Snapshot Empty { get; } = new(
                new Dictionary<Guid, PosterSettings>(),
                new Dictionary<Guid, LogoSettings>(),
                new PosterSettings(),
                Array.Empty<LogoConfiguration>(),
                new LogoSettings(),
                new ArtworkProfile { IsDefault = true },
                new Dictionary<Guid, ArtworkProfile>(),
                new Dictionary<Guid, ArtworkProfile>());
        }
    }
}
