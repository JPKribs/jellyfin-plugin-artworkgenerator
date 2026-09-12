using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Configuration;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtworkGenerator.Services
{
    /// <summary>
    /// TODO (12.0.2.1): delete this file.
    ///
    /// Everything here is a one-time upgrade of a configuration written before 12.0.2.0, kept
    /// apart from the service proper so it can be removed in one piece rather than picked out of
    /// the code it is tangled with. A migration only ever reads a name nothing writes any more, so
    /// once every install has started on 12.0.2.0 and saved, none of it does anything.
    ///
    /// To remove it, delete:
    ///   * this file
    ///   * Models/Poster/PosterSettings.Legacy.cs, the former setting names it reads
    ///   * PluginConfiguration.FrameExtractionMigrated, the flag that runs one of these once
    ///   * PosterConfiguration.SeriesIds, which profiles replaced
    ///   * the two calls in Initialize marked with the same TODO
    ///   * the tests in ConfigurationMigrationTests.cs and FrameEdgeMigrationTests.cs
    ///
    /// Nothing outside those points at this file, and the build fails on anything missed.
    /// </summary>
    public partial class PosterConfigurationService
    {
        // MigrateConfiguration
        // Every upgrade that needs nothing but the configuration and its default design. Runs
        // before the logo designs are loaded, because one of these moves them into their own file.
        private void MigrateConfiguration(PluginConfiguration config, PosterConfiguration design)
        {
            MigrateLegacySettings(config);
            MigrateFrameExtraction(config);
            RetireSynthesizedPortraitDesign(config, design);
            MigrateLogoDesignsIntoStore(config);
        }

        // MigrateLegacyDesignsToProfiles
        // Before profiles, a design carried the series it applied to. Each such design becomes a
        // profile holding those series, so a series keeps the episode poster design it had. The
        // default profile is created here too, because a configuration that needs this one has no
        // profiles at all and the bootstrap would otherwise make the first legacy profile default.
        private void MigrateLegacyDesignsToProfiles(PluginConfiguration config, PosterConfiguration design, LogoConfiguration logo)
        {
            if (config.Profiles.Count > 0)
            {
                return;
            }

            var legacyDesigns = config.PosterConfigurations
                .Where(c => !c.IsDefault && c.SeriesIds.Count > 0)
                .ToList();

            if (legacyDesigns.Count == 0)
            {
                return;
            }

            config.Profiles.Add(CreateProfile(DefaultProfileId, DefaultName, true, design, design, logo));

            foreach (var legacy in legacyDesigns)
            {
                var profile = CreateProfile(legacy.Id, legacy.Name, false, legacy, design, logo);
                profile.SeriesIds.AddRange(legacy.SeriesIds);
                legacy.SeriesIds.Clear();
                config.Profiles.Add(profile);
            }

            _logger.LogInformation("Created {Count} profile(s) from the existing designs", config.Profiles.Count);
        }

        // MigrateLogoDesignsIntoStore
        // Logo designs once lived in the plugin configuration. They move into their own file on
        // first load and the configuration's copy is dropped, which the next save persists.
        private void MigrateLogoDesignsIntoStore(PluginConfiguration config)
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

            // An earlier build called the synthesized design "Default Logo"; every synthesized
            // default is simply "Default".
            foreach (var logo in logos.Where(l => l.Id == DefaultLogoDesignId && l.Name == "Default Logo"))
            {
                logo.Name = DefaultName;
                changed = true;
            }

            if (changed)
            {
                foreach (var logo in logos)
                {
                    logo.Settings ??= new LogoSettings();
                }

                _logoStore.Save(logos);
            }
        }

        // MigrateLegacySettings
        // Brings older designs forward in memory: the pre-10.11.23 ExtractPoster boolean becomes a
        // CanvasSource, and a per-axis graphic size becomes the single box size.
        private void MigrateLegacySettings(PluginConfiguration config)
        {
            var migrated = false;

            foreach (var posterConfig in config.PosterConfigurations)
            {
                var settings = posterConfig.Settings;
                migrated |= MigrateTextVocabulary(settings);
                if (settings.ExtractPoster.HasValue)
                {
                    settings.CanvasSource = settings.ExtractPoster.Value ? CanvasSource.Extract : CanvasSource.None;
                    settings.ExtractPoster = null;
                    migrated = true;
                }

                // A graphic used to be sized per axis, which could stretch it. The larger of the two
                // becomes the box it is now fitted inside.
                if (settings.GraphicWidth.HasValue || settings.GraphicHeight.HasValue)
                {
                    var size = Math.Max(settings.GraphicWidth ?? 0f, settings.GraphicHeight ?? 0f);
                    if (size > 0f)
                    {
                        settings.GraphicSize = size;
                    }

                    settings.GraphicWidth = null;
                    settings.GraphicHeight = null;
                    migrated = true;
                }
            }

            if (migrated)
            {
                _logger.LogInformation("Brought legacy design settings forward to the current names");
            }
        }


        // MigrateFrameExtraction
        // These were once a copy on every design and every profile's backdrop. The default design's
        // values are the ones the user actually set, so they become the server's, once.
        private void MigrateFrameExtraction(PluginConfiguration config)
        {
            if (config.FrameExtractionMigrated)
            {
                return;
            }

            config.FrameExtractionMigrated = true;

            var source = config.PosterConfigurations.FirstOrDefault(c => c.IsDefault)?.Settings
                ?? config.PosterConfigurations.FirstOrDefault()?.Settings;

            if (source == null)
            {
                return;
            }

            config.FrameExtraction = new FrameExtractionSettings
            {
                ExtractWindowStart = source.ExtractWindowStart,
                ExtractWindowEnd = source.ExtractWindowEnd,
                BrightenFrame = source.BrightenHDR,
                EnableLetterboxDetection = source.EnableLetterboxDetection,
                LetterboxBlackThreshold = source.LetterboxBlackThreshold,
                LetterboxConfidence = source.LetterboxConfidence
            };

            _logger.LogInformation(
                "Brought the frame extraction settings forward from the default design: {Start} to {End} percent",
                config.FrameExtraction.ExtractWindowStart,
                config.FrameExtraction.ExtractWindowEnd);
        }

        // MigrateTextVocabulary
        // These settings were once named for a title and an episode; they are now named for the
        // primary and secondary lines every style draws. A value saved under an old name is copied
        // onto its replacement and cleared, so an existing design keeps the fonts and colors the
        // user chose rather than quietly reverting to the defaults.
        internal static bool MigrateTextVocabulary(PosterSettings settings)
        {
            var migrated = false;

            // A framed design used to name its own edges. The title's edge is now the ordinary text
            // position, and whether a lone line follows it is what the "first" choices really meant.
            if (settings.TextEdge is { } edge)
            {
                settings.TextPosition = edge is TextEdge.BottomFirst or TextEdge.AlwaysBottom
                    ? TextPosition.Bottom
                    : TextPosition.Top;
                settings.LoneLineFollowsTitle = edge is TextEdge.TopFirst or TextEdge.BottomFirst;
                settings.TextEdge = null;
                migrated = true;
            }

            if (settings.ShowTitle is { } showPrimary)
            {
                settings.ShowPrimary = showPrimary;
                settings.ShowTitle = null;
                migrated = true;
            }

            if (settings.ShowEpisode is { } showSecondary)
            {
                settings.ShowSecondary = showSecondary;
                settings.ShowEpisode = null;
                migrated = true;
            }

            if (settings.TitleUseCustomFont is { } primaryUseCustomFont)
            {
                settings.PrimaryUseCustomFont = primaryUseCustomFont;
                settings.TitleUseCustomFont = null;
                migrated = true;
            }

            if (settings.EpisodeUseCustomFont is { } secondaryUseCustomFont)
            {
                settings.SecondaryUseCustomFont = secondaryUseCustomFont;
                settings.EpisodeUseCustomFont = null;
                migrated = true;
            }

            if (settings.TitleFontSize is { } primaryFontSize)
            {
                settings.PrimaryFontSize = primaryFontSize;
                settings.TitleFontSize = null;
                migrated = true;
            }

            if (settings.EpisodeFontSize is { } secondaryFontSize)
            {
                settings.SecondaryFontSize = secondaryFontSize;
                settings.EpisodeFontSize = null;
                migrated = true;
            }

            if (settings.TitleEdge is { } textEdge)
            {
                settings.TextEdge = textEdge;
                settings.TitleEdge = null;
                migrated = true;
            }

            if (settings.LongTitleHandling is { } longTextHandling)
            {
                settings.LongTextHandling = longTextHandling;
                settings.LongTitleHandling = null;
                migrated = true;
            }

            if (settings.TitleFontFamily != null)
            {
                if (settings.TitleFontFamily.Length > 0)
                {
                    settings.PrimaryFontFamily = settings.TitleFontFamily;
                }

                settings.TitleFontFamily = null;
                migrated = true;
            }

            if (settings.TitleFontPath != null)
            {
                if (settings.TitleFontPath.Length > 0)
                {
                    settings.PrimaryFontPath = settings.TitleFontPath;
                }

                settings.TitleFontPath = null;
                migrated = true;
            }

            if (settings.TitleFontStyle != null)
            {
                if (settings.TitleFontStyle.Length > 0)
                {
                    settings.PrimaryFontStyle = settings.TitleFontStyle;
                }

                settings.TitleFontStyle = null;
                migrated = true;
            }

            if (settings.TitleFontColor != null)
            {
                if (settings.TitleFontColor.Length > 0)
                {
                    settings.PrimaryFontColor = settings.TitleFontColor;
                }

                settings.TitleFontColor = null;
                migrated = true;
            }

            if (settings.EpisodeFontFamily != null)
            {
                if (settings.EpisodeFontFamily.Length > 0)
                {
                    settings.SecondaryFontFamily = settings.EpisodeFontFamily;
                }

                settings.EpisodeFontFamily = null;
                migrated = true;
            }

            if (settings.EpisodeFontPath != null)
            {
                if (settings.EpisodeFontPath.Length > 0)
                {
                    settings.SecondaryFontPath = settings.EpisodeFontPath;
                }

                settings.EpisodeFontPath = null;
                migrated = true;
            }

            if (settings.EpisodeFontStyle != null)
            {
                if (settings.EpisodeFontStyle.Length > 0)
                {
                    settings.SecondaryFontStyle = settings.EpisodeFontStyle;
                }

                settings.EpisodeFontStyle = null;
                migrated = true;
            }

            if (settings.EpisodeFontColor != null)
            {
                if (settings.EpisodeFontColor.Length > 0)
                {
                    settings.SecondaryFontColor = settings.EpisodeFontColor;
                }

                settings.EpisodeFontColor = null;
                migrated = true;
            }

            return migrated;
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
    }
}
