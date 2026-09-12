using System.Collections.Generic;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Posters
{
    /// <summary>
    /// What a poster style does with one setting.
    /// </summary>
    public enum PosterSettingState
    {
        /// <summary>The style honours the setting, and the user chooses.</summary>
        Optional,

        /// <summary>The style always draws this element, so the toggle is on and not offered.</summary>
        Required,

        /// <summary>The style ignores the setting, so it is not offered.</summary>
        Hidden
    }

    /// <summary>
    /// Which settings each poster style actually uses, declared by the generators themselves so the
    /// configuration page can build its form from the styles rather than keeping its own copy of
    /// the rules. Keys match the setting names on <see cref="Models.PosterSettings"/>.
    /// </summary>
    public static class PosterSettingRules
    {
        /// <summary>Setting names shared by the rules below.</summary>
        public const string ShowPrimary = "ShowPrimary";

        /// <summary>The number and code toggle.</summary>
        public const string ShowSecondary = "ShowSecondary";

        /// <summary>The number and code size.</summary>
        public const string SecondaryFontSize = "SecondaryFontSize";

        /// <summary>The number and code colour.</summary>
        public const string SecondaryFontColor = "SecondaryFontColor";

        /// <summary>Which edge of a framed poster the title sits in.</summary>
        public const string TextEdge = "TextEdge";

        /// <summary>The cutout's text choice.</summary>
        public const string CutoutType = "CutoutType";

        /// <summary>The outline drawn around a cut-out shape.</summary>
        public const string CutoutBorder = "CutoutBorder";

        /// <summary>The series logo's vertical placement.</summary>
        public const string LogoPosition = "LogoPosition";

        /// <summary>The series logo's horizontal placement.</summary>
        public const string LogoAlignment = "LogoAlignment";

        /// <summary>The series logo's height.</summary>
        public const string LogoHeight = "LogoHeight";

        /// <summary>
        /// Gets the rules that apply to a style which draws none of the style-specific elements:
        /// the cutout, outline, and series-logo settings are not offered.
        /// </summary>
        public static IReadOnlyDictionary<string, PosterSettingState> None { get; } = Build();

        /// <summary>
        /// Builds a style's rules: the style-specific settings start hidden, and
        /// <paramref name="overrides"/> names the ones this style uses or insists on.
        /// </summary>
        /// <param name="overrides">The settings whose state differs from the default.</param>
        /// <returns>The rules for one style, keyed by setting name.</returns>
        public static IReadOnlyDictionary<string, PosterSettingState> Build(params (string Setting, PosterSettingState State)[] overrides)
        {
            var rules = new Dictionary<string, PosterSettingState>(System.StringComparer.Ordinal)
            {
                [TextEdge] = PosterSettingState.Hidden,
                [CutoutType] = PosterSettingState.Hidden,
                [CutoutBorder] = PosterSettingState.Hidden,
                [LogoPosition] = PosterSettingState.Hidden,
                [LogoAlignment] = PosterSettingState.Hidden,
                [LogoHeight] = PosterSettingState.Hidden
            };

            if (overrides != null)
            {
                foreach (var (setting, state) in overrides)
                {
                    rules[setting] = state;
                }
            }

            return rules;
        }
    }
}
