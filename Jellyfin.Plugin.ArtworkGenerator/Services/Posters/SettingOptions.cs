using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Posters
{
    /// <summary>The words a page shows for one setting.</summary>
    /// <param name="Label">The field's label.</param>
    /// <param name="Description">The help text beneath it, empty when the field needs none.</param>
    public sealed record SettingText(string Label, string Description);

    /// <summary>One choice offered for a setting.</summary>
    /// <param name="Value">The value stored in the design.</param>
    /// <param name="Label">What the configuration page shows for it.</param>
    public sealed record SettingOption(string Value, string Label);

    /// <summary>
    /// The choices and defaults for every setting on <see cref="PosterSettings"/>, read from the
    /// settings themselves. The configuration page builds its dropdowns from this rather than
    /// keeping its own copy of each enum, so adding a value to an enum offers it in the UI with no
    /// change to the page.
    /// </summary>
    public static class SettingOptions
    {
        // Settings whose choices are not an enum: a font style is a string, and a line count is a
        // number with only two sensible values.
        private static readonly Dictionary<string, IReadOnlyList<SettingOption>> Explicit = new(StringComparer.Ordinal)
        {
            ["PrimaryFontStyle"] = FontStyleOptions(),
            ["SecondaryFontStyle"] = FontStyleOptions(),
            ["FontStyle"] = FontStyleOptions(),
            ["MaxLines"] = new[]
            {
                new SettingOption("1", "One line"),
                new SettingOption("2", "Up to two lines")
            }
        };

        private static List<SettingOption> FontStyleOptions()
        {
            return FontUtils.FontStyles.Select(style => new SettingOption(style, style)).ToList();
        }

        // Settable
        // The settings a design actually stores: writable and not computed.
        private static IEnumerable<PropertyInfo> Settable()
        {
            return typeof(PosterSettings)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite)
                .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() == null);
        }

        /// <summary>
        /// Gets the choices for each setting that has a fixed set of them, keyed by setting name.
        /// </summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<SettingOption>> All()
        {
            var options = new Dictionary<string, IReadOnlyList<SettingOption>>(StringComparer.Ordinal);

            foreach (var property in Settable().Concat(SettableLogoProperties()))
            {
                if (property.PropertyType.IsEnum)
                {
                    options[property.Name] = Choices(property.PropertyType);
                }
                else if (Explicit.TryGetValue(property.Name, out var values))
                {
                    options[property.Name] = values;
                }
            }

            // Not a design setting, so it is not on PosterSettings: the Designs page's preview picker
            // offers the kinds of item a preview can draw, which are the item kinds themselves. It is
            // served here so that page has no hardcoded list of its own either.
            options["PreviewKind"] = Choices(typeof(ArtworkItemKind));

            return options;
        }

        // Choices
        // An enum's values as options. The poster styles are the one list a reader scans rather than
        // recognizes, so it leads with the plain one and alphabetizes the rest. Every other enum
        // keeps the order it is declared in, which is usually meaningful.
        private static List<SettingOption> Choices(Type type)
        {
            var choices = type
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field => new SettingOption(field.Name, LabelFor(field)))
                .ToList();

            if (type != typeof(PosterStyle))
            {
                return choices;
            }

            return choices
                .OrderByDescending(choice => string.Equals(choice.Value, nameof(PosterStyle.Standard), StringComparison.Ordinal))
                .ThenBy(choice => choice.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Gets the label and help text for every setting, read from the settings models. The
        /// configuration pages render these rather than carrying their own copy, so the wording of a
        /// setting is changed in one place, next to the setting itself.
        /// </summary>
        public static IReadOnlyDictionary<string, SettingText> Text()
        {
            var text = new Dictionary<string, SettingText>(StringComparer.Ordinal);

            foreach (var property in Settable().Concat(SettableLogoProperties()))
            {
                var display = property.GetCustomAttribute<DisplayAttribute>();
                if (display?.Name == null)
                {
                    continue;
                }

                text[property.Name] = new SettingText(display.Name, display.Description ?? string.Empty);
            }

            return text;
        }

        /// <summary>
        /// Gets the label and help text for the backdrop settings. These keep their own map because
        /// several of their names — the extraction window, the letterbox thresholds — are also
        /// design setting names, and one dictionary would let the two overwrite each other.
        /// </summary>
        /// <summary>
        /// Gets the label and help text for the frame extraction settings, which live on the server
        /// rather than on a design and so are not in either of the maps above.
        /// </summary>
        public static IReadOnlyDictionary<string, SettingText> FrameExtractionText()
        {
            return TextFrom(typeof(FrameExtractionSettings));
        }

        // TextFrom
        // Every Display attribute on a settings model, keyed by property name.
        private static Dictionary<string, SettingText> TextFrom(Type type)
        {
            var text = new Dictionary<string, SettingText>(StringComparer.Ordinal);

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var display = property.GetCustomAttribute<DisplayAttribute>();
                if (display?.Name == null)
                {
                    continue;
                }

                text[property.Name] = new SettingText(display.Name, display.Description ?? string.Empty);
            }

            return text;
        }

        public static IReadOnlyDictionary<string, SettingText> BackdropText()
        {
            var text = new Dictionary<string, SettingText>(StringComparer.Ordinal);

            foreach (var property in typeof(BackdropSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var display = property.GetCustomAttribute<DisplayAttribute>();
                if (display?.Name == null)
                {
                    continue;
                }

                text[property.Name] = new SettingText(display.Name, display.Description ?? string.Empty);
            }

            return text;
        }

        // SettableLogoProperties
        // A logo design's settings, which the Logos page renders the same way.
        private static IEnumerable<PropertyInfo> SettableLogoProperties()
        {
            return typeof(LogoSettings)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite)
                .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() == null);
        }

        /// <summary>
        /// Gets the value each setting takes on a design that has not been configured, so a new
        /// design starts from the same defaults the renderer would apply.
        /// </summary>
        public static IReadOnlyDictionary<string, object?> Defaults()
        {
            var defaults = new PosterSettings();
            var logoDefaults = new LogoSettings();

            return Settable().Select(p => (Property: p, Source: (object)defaults))
                .Concat(SettableLogoProperties().Select(p => (Property: p, Source: (object)logoDefaults)))
                .ToDictionary(
                entry => entry.Property.Name,
                entry =>
                {
                    var value = entry.Property.GetValue(entry.Source);
                    return entry.Property.PropertyType.IsEnum ? value?.ToString() : value;
                },
                StringComparer.Ordinal);
        }

        /// <summary>
        /// Gets the defaults for a logo design alone. The merged map above answers "what is this one
        /// setting's default"; this answers "what does a brand new logo design look like", which must
        /// not carry a poster design's settings with it.
        /// </summary>
        public static IReadOnlyDictionary<string, object?> LogoDefaults()
        {
            var defaults = new LogoSettings();

            return SettableLogoProperties().ToDictionary(
                property => property.Name,
                property =>
                {
                    var value = property.GetValue(defaults);
                    return property.PropertyType.IsEnum ? value?.ToString() : value;
                },
                StringComparer.Ordinal);
        }

        // LabelFor
        // An enum member's own Description when it carries one, since several read as prose rather
        // than as their member name ("Extract Frame from Video"). Everything else is the member
        // name with its words separated.
        private static string LabelFor(FieldInfo field)
        {
            var described = field.GetCustomAttribute<DescriptionAttribute>();
            return described != null ? described.Description : Humanize(field.Name);
        }

        // Humanize
        // Splits a PascalCase name into words: FrostedGlass becomes "Frosted Glass".
        private static string Humanize(string name)
        {
            var builder = new StringBuilder(name.Length + 4);

            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                {
                    builder.Append(' ');
                }

                builder.Append(name[i]);
            }

            return builder.ToString();
        }
    }
}
