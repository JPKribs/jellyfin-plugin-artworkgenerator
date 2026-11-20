using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Posters
{
    /// <summary>The words a page shows for one setting, as translation keys on a model or as text once translated.</summary>
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
    /// change to the page. Every label and help text is a translation key on the model, given back
    /// in the language asked for.
    /// </summary>
    public static class SettingOptions
    {
        // The label key for each font style string FontUtils understands.
        private static readonly Dictionary<string, string> FontStyleKeys = new(StringComparer.Ordinal)
        {
            ["Normal"] = TranslationKeys.Normal,
            ["Bold"] = TranslationKeys.Bold,
            ["Italic"] = TranslationKeys.OptionItalic,
            ["Bold Italic"] = TranslationKeys.OptionBoldItalic
        };

        // Settings whose choices are not an enum: a font style is a string, and a line count is a
        // number with only two sensible values. Each label is a translation key.
        private static readonly Dictionary<string, IReadOnlyList<SettingOption>> Explicit = new(StringComparer.Ordinal)
        {
            ["PrimaryFontStyle"] = FontStyleOptions(),
            ["SecondaryFontStyle"] = FontStyleOptions(),
            ["FontStyle"] = FontStyleOptions(),
            ["MaxLines"] = new[]
            {
                new SettingOption("1", TranslationKeys.OptionOneLine),
                new SettingOption("2", TranslationKeys.OptionUpToTwoLines)
            }
        };

        private static List<SettingOption> FontStyleOptions()
        {
            return FontUtils.FontStyles
                .Select(style => new SettingOption(style, FontStyleKeys.TryGetValue(style, out var key) ? key : style))
                .ToList();
        }

        /// <summary>
        /// Translates a setting's label and help text.
        /// </summary>
        /// <param name="text">The label and help text as translation keys.</param>
        /// <param name="language">The viewer's language, or <c>null</c> for US English.</param>
        /// <returns>The label and help text in that language.</returns>
        public static SettingText Translate(SettingText text, string? language)
        {
            ArgumentNullException.ThrowIfNull(text);
            return new SettingText(
                BaseTranslations.Get(text.Label, language),
                string.IsNullOrEmpty(text.Description) ? string.Empty : BaseTranslations.Get(text.Description, language));
        }

        // TranslateAll
        // Translates every label and help text in a map of setting text.
        private static Dictionary<string, SettingText> TranslateAll(IReadOnlyDictionary<string, SettingText> text, string? language)
        {
            return text.ToDictionary(entry => entry.Key, entry => Translate(entry.Value, language), StringComparer.Ordinal);
        }

        // TranslateOptions
        // Translates the label of every choice in a list.
        private static List<SettingOption> TranslateOptions(IEnumerable<SettingOption> options, string? language)
        {
            return options.Select(option => new SettingOption(option.Value, BaseTranslations.Get(option.Label, language))).ToList();
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
        /// <param name="language">The viewer's language, or <c>null</c> for US English.</param>
        /// <returns>The choices, each labeled in that language.</returns>
        public static IReadOnlyDictionary<string, IReadOnlyList<SettingOption>> All(string? language = null)
        {
            var options = new Dictionary<string, IReadOnlyList<SettingOption>>(StringComparer.Ordinal);

            foreach (var property in Settable().Concat(SettableLogoProperties()))
            {
                if (property.PropertyType.IsEnum)
                {
                    options[property.Name] = Choices(property.PropertyType, language);
                }
                else if (Explicit.TryGetValue(property.Name, out var values))
                {
                    options[property.Name] = TranslateOptions(values, language);
                }
            }

            // Not a design setting, so it is not on PosterSettings: the Designs page's preview picker
            // offers the kinds of item a preview can draw, which are the item kinds themselves. It is
            // served here so that page has no hardcoded list of its own either. A library gets no
            // artwork, so it is left out.
            options["PreviewKind"] = Choices(typeof(ArtworkItemKind), language)
                .Where(option => option.Value != nameof(ArtworkItemKind.Library))
                .ToList();

            return options;
        }

        // Choices
        // An enum's values as options, labeled in a language. The poster styles are the one list a
        // reader scans rather than recognizes, so it leads with the plain one and alphabetizes the
        // rest in that language. Every other enum keeps the order it is declared in, which is
        // usually meaningful.
        private static List<SettingOption> Choices(Type type, string? language)
        {
            var choices = type
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field => new SettingOption(field.Name, BaseTranslations.Get(LabelFor(field), language)))
                .ToList();

            if (type != typeof(PosterStyle))
            {
                return choices;
            }

            return choices
                .OrderByDescending(choice => string.Equals(choice.Value, nameof(PosterStyle.Standard), StringComparison.Ordinal))
                .ThenBy(choice => choice.Label, StringComparer.Create(Culture(language), ignoreCase: true))
                .ToList();
        }

        /// <summary>
        /// Gets the label and help text for every setting, read from the settings models. The
        /// configuration pages render these rather than carrying their own copy, so the wording of a
        /// setting is changed in one place, next to the setting itself.
        /// </summary>
        /// <param name="language">The viewer's language, or <c>null</c> for US English.</param>
        /// <returns>The label and help text of each setting in that language.</returns>
        public static IReadOnlyDictionary<string, SettingText> Text(string? language = null)
        {
            var text = new Dictionary<string, SettingText>(StringComparer.Ordinal);

            foreach (var property in Settable().Concat(SettableLogoProperties()))
            {
                var display = property.GetCustomAttribute<DisplayAttribute>();
                if (display?.Name == null)
                {
                    continue;
                }

                text[property.Name] = Translate(new SettingText(display.Name, display.Description ?? string.Empty), language);
            }

            return text;
        }

        /// <summary>
        /// Gets the label and help text for the frame extraction settings, which live on the server
        /// rather than on a design and so are not in either of the maps above.
        /// </summary>
        /// <param name="language">The viewer's language, or <c>null</c> for US English.</param>
        /// <returns>The label and help text of each setting in that language.</returns>
        public static IReadOnlyDictionary<string, SettingText> FrameExtractionText(string? language = null)
        {
            return TranslateAll(TextFrom(typeof(FrameExtractionSettings)), language);
        }

        /// <summary>
        /// Gets the choices for each frame extraction setting that has a fixed set of them, keyed by
        /// setting name.
        /// </summary>
        /// <param name="language">The viewer's language, or <c>null</c> for US English.</param>
        /// <returns>The choices, each labeled in that language.</returns>
        public static IReadOnlyDictionary<string, IReadOnlyList<SettingOption>> FrameExtractionOptions(string? language = null)
        {
            return typeof(FrameExtractionSettings)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.PropertyType.IsEnum)
                .ToDictionary(
                    property => property.Name,
                    property => (IReadOnlyList<SettingOption>)Choices(property.PropertyType, language),
                    StringComparer.Ordinal);
        }

        // TextFrom
        // Every Display attribute on a settings model, keyed by property name, as translation keys.
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

        /// <summary>
        /// Gets the label and help text for the backdrop settings, which live on a profile rather
        /// than on a design and so are kept in a map of their own.
        /// </summary>
        /// <param name="language">The viewer's language, or <c>null</c> for US English.</param>
        /// <returns>The label and help text of each setting in that language.</returns>
        public static IReadOnlyDictionary<string, SettingText> BackdropText(string? language = null)
        {
            return TranslateAll(TextFrom(typeof(BackdropSettings)), language);
        }

        /// <summary>
        /// Gets each poster style with its description, the shapes it can lay out, the settings it
        /// uses, and its own wording for a shared setting, in a language.
        /// </summary>
        /// <param name="language">The viewer's language, or <c>null</c> for US English.</param>
        /// <returns>One entry per style, in the order the styles are registered.</returns>
        public static IEnumerable<object> Styles(string? language = null)
        {
            return PreviewService.GetStyleCatalog()
                .Select(g => new
                {
                    value = g.Style.ToString(),
                    description = BaseTranslations.Get(g.Description, language),
                    primaryDescription = BaseTranslations.Get(g.PrimaryDescription, language),
                    secondaryDescription = BaseTranslations.Get(g.SecondaryDescription, language),
                    portrait = g.Supports(ArtworkShape.Portrait),
                    landscape = g.Supports(ArtworkShape.Landscape),
                    settings = g.SettingRules.ToDictionary(rule => rule.Key, rule => rule.Value.ToString()),
                    text = TranslateAll(g.SettingText, language)
                });
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
        // An enum member's label key, from the Description attribute every member a page offers
        // carries. A member with none gives its own name.
        private static string LabelFor(FieldInfo field)
        {
            return field.GetCustomAttribute<DescriptionAttribute>()?.Description ?? field.Name;
        }

        // Culture
        // The culture a language sorts by, or the invariant one when it is not a known culture.
        private static CultureInfo Culture(string? language)
        {
            var code = BaseTranslations.NormalizeLanguage(language);
            if (code.Length == 0)
            {
                return CultureInfo.InvariantCulture;
            }

            try
            {
                return CultureInfo.GetCultureInfo(code);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }
    }
}
