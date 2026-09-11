using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.EpisodePosterGenerator.Configuration;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Services.Artwork;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Services
{
    /// <summary>
    /// Resolves which profile, poster design, and logo design apply to an item, and brings older
    /// configurations up to the profile model.
    /// </summary>
    public class PosterConfigurationService
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

            MigrateLegacySettings(config);

            var design = EnsureDefaultDesign(config);
            RetireSynthesizedPortraitDesign(config, design);
            var logoDesigns = LoadLogoDesigns(config);
            var logo = logoDesigns[0];

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
            }

            // Atomic swap: readers see either the old or the new state, never a partial one.
            _snapshot = new Snapshot(
                designs,
                logos,
                design.Settings,
                logoDesigns,
                logo.Settings,
                config.Profiles.First(p => p.IsDefault),
                bySeries);

            _logger.LogInformation(
                "Artwork configuration loaded: {Designs} design(s), {Logos} logo design(s), {Profiles} profile(s), {Assigned} assigned series, {Duplicates} duplicate assignment(s) ignored",
                designs.Count,
                logos.Count,
                config.Profiles.Count,
                bySeries.Count,
                duplicates);
        }

        /// <summary>
        /// Returns the profile a series is assigned to, or the default profile.
        /// </summary>
        public ArtworkProfile GetProfileForSeries(Guid seriesId)
        {
            var snapshot = _snapshot;
            return seriesId != Guid.Empty && snapshot.BySeries.TryGetValue(seriesId, out var profile)
                ? profile
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

        // MigrateLegacySettings
        // Migrates the pre-10.11.23 ExtractPoster boolean to the CanvasSource enum, in memory.
        private void MigrateLegacySettings(PluginConfiguration config)
        {
            var migrated = false;

            foreach (var posterConfig in config.PosterConfigurations)
            {
                var settings = posterConfig.Settings;
                if (settings.ExtractPoster.HasValue)
                {
                    settings.CanvasSource = settings.ExtractPoster.Value ? CanvasSource.Extract : CanvasSource.None;
                    settings.ExtractPoster = null;
                    migrated = true;
                }
            }

            if (migrated)
            {
                _logger.LogInformation("Migrated legacy ExtractPoster setting to CanvasSource");
            }
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

        // RetireSynthesizedPortraitDesign
        // An earlier build of this version created a separate "Default Portrait" design, before
        // every design learned to render both shapes. It is dropped in memory and anything that
        // pointed at it points at the default design instead; the user's next save persists that.
        private void RetireSynthesizedPortraitDesign(PluginConfiguration config, PosterConfiguration design)
        {
            var removed = config.PosterConfigurations.RemoveAll(c => c.Id == DefaultPortraitDesignId && !c.IsDefault);
            if (removed == 0)
            {
                return;
            }

            foreach (var slot in config.Profiles.SelectMany(p => p.Slots).Where(s => s.DesignId == DefaultPortraitDesignId))
            {
                slot.DesignId = design.Id;
            }

            _logger.LogInformation("Retired the separate portrait design; every design now renders both shapes");
        }

        // LoadLogoDesigns
        // Logo designs live in their own file beside the configuration, so a logo edit never
        // rewrites the whole configuration. Designs left in an older configuration move across on
        // first load and the configuration's copy is dropped, which the next save persists.
        private List<LogoConfiguration> LoadLogoDesigns(PluginConfiguration config)
        {
            var logos = _logoStore.Load().ToList();
            var changed = false;

            if (config.LogoConfigurations.Count > 0)
            {
                if (logos.Count == 0)
                {
                    logos = config.LogoConfigurations.ToList();
                    _logger.LogInformation(
                        "Moved {Count} logo design(s) out of the plugin configuration into their own file",
                        logos.Count);
                }

                config.LogoConfigurations.Clear();
                changed = true;
            }

            if (logos.Count == 0)
            {
                logos.Add(new LogoConfiguration { Id = DefaultLogoDesignId, Name = DefaultName });
                changed = true;
            }

            // An earlier build called the synthesized design "Default Logo"; every synthesized
            // default is simply "Default".
            foreach (var logo in logos.Where(l => l.Id == DefaultLogoDesignId && l.Name == "Default Logo"))
            {
                logo.Name = DefaultName;
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
        // Builds profiles for a configuration that predates them. The default profile reproduces
        // the old behaviour for episodes and turns on the new slots with the default designs; every
        // design that had series assigned becomes a profile carrying those series, so each series
        // keeps the episode poster design it had.
        private void EnsureProfiles(PluginConfiguration config, PosterConfiguration design, LogoConfiguration logo)
        {
            if (config.Profiles.Count == 0)
            {
                config.Profiles.Add(CreateProfile(DefaultProfileId, DefaultName, true, design, design, logo));

                foreach (var legacy in config.PosterConfigurations.Where(c => !c.IsDefault && c.SeriesIds.Count > 0))
                {
                    var profile = CreateProfile(legacy.Id, legacy.Name, false, legacy, design, logo);
                    profile.SeriesIds.AddRange(legacy.SeriesIds);
                    legacy.SeriesIds.Clear();
                    config.Profiles.Add(profile);
                }

                _logger.LogInformation("Created {Count} profile(s) from the existing designs", config.Profiles.Count);
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

        private static ArtworkProfile CreateProfile(
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
                Backdrop = new BackdropSettings
                {
                    EnableLetterboxDetection = source.EnableLetterboxDetection,
                    LetterboxBlackThreshold = source.LetterboxBlackThreshold,
                    LetterboxConfidence = source.LetterboxConfidence,
                    BrightenHDR = source.BrightenHDR,
                    ExtractWindowStart = source.ExtractWindowStart,
                    ExtractWindowEnd = source.ExtractWindowEnd
                }
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

        private static void Add(ArtworkProfile profile, ArtworkItemKind kind, ArtworkSlot slot, bool enabled, Guid designId)
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
            IReadOnlyDictionary<Guid, ArtworkProfile> BySeries)
        {
            public static Snapshot Empty { get; } = new(
                new Dictionary<Guid, PosterSettings>(),
                new Dictionary<Guid, LogoSettings>(),
                new PosterSettings(),
                Array.Empty<LogoConfiguration>(),
                new LogoSettings(),
                new ArtworkProfile { IsDefault = true },
                new Dictionary<Guid, ArtworkProfile>());
        }
    }
}
