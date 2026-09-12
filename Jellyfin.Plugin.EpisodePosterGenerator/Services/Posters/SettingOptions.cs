using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Utilities;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Services.Posters
{
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
        // Settings whose values are strings rather than an enum, and the C# that owns the list.
        private static readonly Dictionary<string, IReadOnlyList<string>> StringValued = new(StringComparer.Ordinal)
        {
            ["PrimaryFontStyle"] = FontUtils.FontStyles,
            ["SecondaryFontStyle"] = FontUtils.FontStyles
        };

        // Settable
        // The settings a design actually stores: writable, not computed, and not one of the legacy
        // names kept only for migration (those are the nullable ones).
        private static IEnumerable<PropertyInfo> Settable()
        {
            return typeof(PosterSettings)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite)
                .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() == null)
                .Where(p => Nullable.GetUnderlyingType(p.PropertyType) == null);
        }

        /// <summary>
        /// Gets the choices for each setting that has a fixed set of them, keyed by setting name.
        /// </summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<SettingOption>> All()
        {
            var options = new Dictionary<string, IReadOnlyList<SettingOption>>(StringComparer.Ordinal);

            foreach (var property in Settable())
            {
                if (property.PropertyType.IsEnum)
                {
                    options[property.Name] = property.PropertyType
                        .GetFields(BindingFlags.Public | BindingFlags.Static)
                        .Select(field => new SettingOption(field.Name, LabelFor(field)))
                        .ToList();
                }
                else if (StringValued.TryGetValue(property.Name, out var values))
                {
                    options[property.Name] = values.Select(v => new SettingOption(v, v)).ToList();
                }
            }

            return options;
        }

        /// <summary>
        /// Gets the value each setting takes on a design that has not been configured, so a new
        /// design starts from the same defaults the renderer would apply.
        /// </summary>
        public static IReadOnlyDictionary<string, object?> Defaults()
        {
            var defaults = new PosterSettings();

            return Settable().ToDictionary(
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
