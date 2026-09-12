using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using SkiaSharp;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Posters
{
    /// <summary>
    /// The text half of every design, kept beside the pipeline rather than inside it.
    /// </summary>
    public abstract partial class BasePosterGenerator
    {
        // NaturalTextPosition
        // Where this style puts its text when the setting is left on its default. Almost every
        // design packs the text against the bottom; one built around centered text says so, and
        // then "Design default" means the right thing for it too.
        protected virtual TextPosition NaturalTextPosition => TextPosition.Bottom;

        // ResolveTextAnchor
        // Turns the setting into the edge a LayoutColumn packs against, falling back to the
        // style's own placement so an untouched configuration renders exactly as it always has.
        protected LayoutAnchor ResolveTextAnchor(PosterSettings settings)
        {
            var position = settings?.TextPosition ?? TextPosition.Auto;

            if (position == TextPosition.Auto)
            {
                position = NaturalTextPosition;
            }

            return position switch
            {
                TextPosition.Top => LayoutAnchor.Top,
                TextPosition.Center => LayoutAnchor.Center,
                _ => LayoutAnchor.Bottom
            };
        }

        // NaturalTextAlignment
        // The side this style pulls its text to when the setting is left on its default. Most
        // designs center it; the ones built around a left hand column say so.
        protected virtual TextAlignment NaturalTextAlignment => TextAlignment.Center;

        // ResolveTextAlign
        // Turns the setting into the alignment the text is drawn with, falling back to the style's
        // own side so an untouched configuration renders exactly as it always has.
        protected SKTextAlign ResolveTextAlign(PosterSettings settings)
        {
            var alignment = settings?.TextAlignment ?? TextAlignment.Auto;

            if (alignment == TextAlignment.Auto)
            {
                alignment = NaturalTextAlignment;
            }

            return alignment switch
            {
                TextAlignment.Left => SKTextAlign.Left,
                TextAlignment.Right => SKTextAlign.Right,
                _ => SKTextAlign.Center
            };
        }

        // AlignedX
        // The x a run of text is drawn from for a given alignment: SkiaSharp treats it as the left
        // edge, the middle, or the right edge depending on the alignment it is given.
        protected static float AlignedX(SKRect slot, SKTextAlign align)
        {
            return align switch
            {
                SKTextAlign.Left => slot.Left,
                SKTextAlign.Right => slot.Right,
                _ => slot.MidX
            };
        }

        // PlaceBlockTop
        // The top edge for a block of known height, for the styles that position a measured box
        // themselves — a frosted panel, a centered pair of lines — rather than through a column.
        protected float PlaceBlockTop(SKRect safeArea, float blockHeight, PosterSettings settings)
        {
            return ResolveTextAnchor(settings) switch
            {
                LayoutAnchor.Top => safeArea.Top,
                LayoutAnchor.Center => safeArea.MidY - (blockHeight / 2f),
                _ => safeArea.Bottom - blockHeight
            };
        }

        // ShowsPrimary
        // Whether the design draws the primary line and this item has one to put in it.
        protected static bool ShowsPrimary(PosterSettings settings, ArtworkSubject subject)
            => settings != null && settings.ShowPrimary && !string.IsNullOrWhiteSpace(subject?.Primary);

        // ShowsSecondary
        // Whether the design draws the secondary line and this item has one. Its long and short
        // renderings are present or absent together, so one question answers for either.
        protected static bool ShowsSecondary(PosterSettings settings, ArtworkSubject subject)
        {
            if (settings == null || !settings.ShowSecondary || !(subject?.Secondary.Length > 0))
            {
                return false;
            }

            return !settings.HideRepeatedSubtitle || !RepeatsTheTitle(subject!);
        }

        // RepeatsTheTitle
        // Whether the subtitle adds nothing the title has not already said. An exact repeat is the
        // plain case. The useful one is a season whose name is its number worded differently, such
        // as "Staffel 12" beside a "SEASON 12" label: different strings, one piece of information.
        private static bool RepeatsTheTitle(ArtworkSubject subject)
        {
            var primary = subject.Primary?.Trim();
            if (string.IsNullOrEmpty(primary))
            {
                return false;
            }

            if (string.Equals(primary, subject.Secondary.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!subject.FeaturedNumber.HasValue)
            {
                return false;
            }

            var match = NumberedName.Match(primary);
            return match.Success
                && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var numbered)
                && numbered == subject.FeaturedNumber.Value;
        }

        // CreatePrimaryStyle
        // The font and paints for the episode title, sized and colored from the settings.
        protected static TextStyle CreatePrimaryStyle(PosterSettings settings, int height, SKTextAlign align = SKTextAlign.Center, bool withShadow = true)
        {
            ArgumentNullException.ThrowIfNull(settings);

            var fontSize = FontUtils.CalculateFontSizeFromPercentage(settings.PrimaryFontSize, height);
            var typeface = FontUtils.ResolveTypeface(settings.EffectivePrimaryFontPath, settings.PrimaryFontFamily, FontUtils.GetFontStyle(settings.PrimaryFontStyle));
            return PaintFactory.CreateTextStyle(ColorUtils.ParseHexColor(settings.PrimaryFontColor), fontSize, typeface, height, align, withShadow);
        }

        // CreateSecondaryStyle
        // The font and paints for the episode code or number, sized and colored from the settings.
        protected static TextStyle CreateSecondaryStyle(PosterSettings settings, int height, SKTextAlign align = SKTextAlign.Center, bool withShadow = true)
        {
            ArgumentNullException.ThrowIfNull(settings);

            var fontSize = FontUtils.CalculateFontSizeFromPercentage(settings.SecondaryFontSize, height);
            var typeface = ResolveSecondaryTypeface(settings);
            return PaintFactory.CreateTextStyle(ColorUtils.ParseHexColor(settings.SecondaryFontColor), fontSize, typeface, height, align, withShadow);
        }

        // ResolveSecondaryTypeface
        // The subtitle's typeface in the weight the settings ask for, which is what every caller
        // wanted; the explicit-style overload below is for the few that override the weight.
        protected static SKTypeface ResolveSecondaryTypeface(PosterSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            return ResolveSecondaryTypeface(settings, FontUtils.GetFontStyle(settings.SecondaryFontStyle));
        }

        // ResolveSecondaryTypeface
        // The episode face in the given weight. Not disposed by callers: FontUtils owns the cache.
        protected static SKTypeface ResolveSecondaryTypeface(PosterSettings settings, SKFontStyle style)
        {
            ArgumentNullException.ThrowIfNull(settings);
            return FontUtils.ResolveTypeface(settings.EffectiveSecondaryFontPath, settings.SecondaryFontFamily, style);
        }

        // DrawPrimaryInSlot
        // Fits the title to the slot's height and the given width, centers the resulting lines
        // vertically in the slot, and draws them at x. Returns the number of lines drawn, which
        // is zero when the long title handling drops the title.
        //
        // Title slots are reserved at a fixed two lines so the elements above them cannot shift
        // between episodes with short and long titles. A one line title therefore leaves a line
        // of slack, and centring splits it evenly rather than pooling it at one end.
        protected static int DrawPrimaryInSlot(SKCanvas canvas, string title, TextStyle style, SKRect slot, float x, float maxWidth, LongTextHandling handling)
        {
            ArgumentNullException.ThrowIfNull(style);

            var lines = TextUtils.FitTextLines(title, style.Font, maxWidth, slot.Height, style.LineHeight, handling);
            if (lines.Count == 0)
            {
                return 0;
            }

            style.DrawLines(canvas, lines, x, style.FirstBaselineCentered(slot, lines.Count));
            return lines.Count;
        }

        // DrawTextStack
        // The layout Standard and Split share: an identity line, a rule, and a two line title zone
        // packed against whichever edge the text position asks for. Returns the area the run
        // occupies, and with no canvas measures without drawing, so the layout map can be told what
        // to keep clear before anything is put down. An episode's identity line is its season and
        // episode numbers; a season or series draws its label, such as SEASON 2, instead.
        protected SKRect DrawTextStack(SKCanvas? canvas, SKRect area, ArtworkSubject subject, PosterSettings settings, int unit)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var align = ResolveTextAlign(settings);
            using var primaryStyle = CreatePrimaryStyle(settings, unit, align);
            using var secondaryStyle = CreateSecondaryStyle(settings, unit, align);

            var parts = subject.SecondaryParts;
            var label = subject.Secondary;
            var showSecondary = ShowsSecondary(settings, subject) && (parts.Count > 1 || label.Length > 0);

            // An item can have no title of its own, such as a season named after nothing but its
            // number. Its zone is not reserved, so nothing is left holding empty space.
            var showPrimary = ShowsPrimary(settings, subject);
            var showSeparator = showPrimary && showSecondary;

            var column = new LayoutColumn(area, GetElementSpacing(settings, unit), ResolveTextAnchor(settings))
                .Add(SecondaryBlock, showSecondary ? secondaryStyle.LineBox : 0f)
                .Add(SeparatorBlock, showSeparator ? RenderConstants.SeparatorSlotHeight(unit) : 0f)
                .Add(PrimaryBlock, showPrimary ? primaryStyle.BlockHeight(2) : 0f);

            if (canvas != null && column.TryGetSlot(SecondaryBlock, out var secondarySlot))
            {
                if (parts.Count > 1)
                {
                    DrawSeasonEpisodeInfo(canvas, parts[0], parts[1], secondaryStyle, settings, unit, secondarySlot);
                }
                else
                {
                    DrawFittedLine(canvas, secondaryStyle, label, AlignedX(secondarySlot, align), secondaryStyle.BaselineAtBottom(secondarySlot), secondarySlot.Width * RenderConstants.TextWidthMultiplier, settings.LongSubtitleHandling, subject.SecondaryShort);
                }
            }

            if (canvas != null && column.TryGetSlot(SeparatorBlock, out var separatorSlot))
            {
                DrawSeparatorLine(canvas, settings, unit, separatorSlot);
            }

            if (canvas != null && column.TryGetSlot(PrimaryBlock, out var primarySlot))
            {
                DrawPrimaryInSlot(canvas, subject.Primary!, primaryStyle, primarySlot, AlignedX(primarySlot, align), primarySlot.Width * RenderConstants.TextWidthMultiplier, settings.LongTextHandling);
            }

            return column.Span;
        }

        // DrawnFromCenter
        // SkiaSharp draws from the left edge, the middle, or the right edge depending on the
        // alignment it is given. A caller that knows where a run's center should be converts it
        // here, so a piece lands in the same place whatever the style is aligned to.
        private static float DrawnFromCenter(float centerX, float width, SKTextAlign align)
        {
            return align switch
            {
                SKTextAlign.Left => centerX - (width / 2f),
                SKTextAlign.Right => centerX + (width / 2f),
                _ => centerX
            };
        }

        // DrawFittedLine
        // Draws a single line that cannot be allowed to run past its slot. A subtitle carries
        // something like "SEASON 12 • EPISODE 7", which fits a landscape poster and overruns a
        // portrait one; the long subtitle setting says what it becomes. Whatever is chosen, the
        // line is squeezed as a last resort rather than allowed over the edge.
        protected static void DrawFittedLine(
            SKCanvas canvas,
            TextStyle style,
            string text,
            float x,
            float baseline,
            float maxWidth,
            LongSubtitleHandling handling = LongSubtitleHandling.Shrink,
            string? shortForm = null)
        {
            ArgumentNullException.ThrowIfNull(style);

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (maxWidth <= 0 || style.MeasureWidth(text) <= maxWidth)
            {
                style.Draw(canvas, text, x, baseline);
                return;
            }

            var chosen = text;

            if (handling == LongSubtitleHandling.ShortCode && !string.IsNullOrEmpty(shortForm))
            {
                chosen = shortForm;
                if (style.MeasureWidth(chosen) <= maxWidth)
                {
                    style.Draw(canvas, chosen, x, baseline);
                    return;
                }
            }

            if (handling == LongSubtitleHandling.Ellipsis)
            {
                var trimmed = TextUtils.FitTitleLine(chosen, style.Font, maxWidth, LongTextHandling.Ellipsis);
                style.Draw(canvas, trimmed ?? chosen, x, baseline);
                return;
            }

            var original = style.Font.Size;
            try
            {
                style.Font.Size = Math.Max(original * MinimumFittedScale, original * (maxWidth / style.MeasureWidth(chosen)));

                if (style.MeasureWidth(chosen) <= maxWidth)
                {
                    style.Draw(canvas, chosen, x, baseline);
                    return;
                }

                // Past the floor the line would be too small to read, so what is left is trimmed.
                var trimmed = TextUtils.FitTitleLine(chosen, style.Font, maxWidth, LongTextHandling.Ellipsis);
                style.Draw(canvas, trimmed ?? chosen, x, baseline);
            }
            finally
            {
                style.Font.Size = original;
            }
        }

        // DrawSeasonEpisodeInfo
        // Draws "season • episode" centered on the slot's bottom edge. The bullet uses the
        // regular weight so it does not overpower the numbers beside it.
        protected static void DrawSeasonEpisodeInfo(SKCanvas canvas, int seasonNumber, int episodeNumber, TextStyle secondaryStyle, PosterSettings settings, int height, SKRect slot)
        {
            ArgumentNullException.ThrowIfNull(secondaryStyle);
            ArgumentNullException.ThrowIfNull(settings);

            var align = secondaryStyle.Align;

            using var bulletStyle = PaintFactory.CreateTextStyle(
                secondaryStyle.Fill.Color, secondaryStyle.Size, ResolveSecondaryTypeface(settings, SKFontStyle.Normal), height, align);

            var seasonText = seasonNumber.ToString(CultureInfo.InvariantCulture);
            var episodeText = episodeNumber.ToString(CultureInfo.InvariantCulture);
            const string bulletText = " • ";

            var baselineY = secondaryStyle.BaselineAtBottom(slot);

            var seasonWidth = secondaryStyle.MeasureWidth(seasonText);
            var episodeWidth = secondaryStyle.MeasureWidth(episodeText);
            var bulletWidth = bulletStyle.MeasureWidth(bulletText);

            // The three pieces are laid out around the bullet, so the run is anchored as a whole and
            // then each piece is placed at its own center.
            var bulletCenter = align switch
            {
                SKTextAlign.Left => slot.Left + seasonWidth + (bulletWidth / 2f),
                SKTextAlign.Right => slot.Right - episodeWidth - (bulletWidth / 2f),
                _ => slot.MidX
            };

            secondaryStyle.Draw(canvas, seasonText, DrawnFromCenter(bulletCenter - (bulletWidth / 2f) - (seasonWidth / 2f), seasonWidth, align), baselineY);
            bulletStyle.Draw(canvas, bulletText, DrawnFromCenter(bulletCenter, bulletWidth, align), baselineY);
            secondaryStyle.Draw(canvas, episodeText, DrawnFromCenter(bulletCenter + (bulletWidth / 2f) + (episodeWidth / 2f), episodeWidth, align), baselineY);
        }

        // DrawSeparatorLine
        // Draws a horizontal rule across the slot in the episode color, with a shadow.
        protected static void DrawSeparatorLine(SKCanvas canvas, PosterSettings settings, int height, SKRect slot)
        {
            ArgumentNullException.ThrowIfNull(settings);

            var stroke = RenderConstants.SeparatorStrokeWidth(height);
            using var shadowPaint = PaintFactory.CreateShadowLinePaint(stroke);
            using var linePaint = PaintFactory.CreateLinePaint(ColorUtils.ParseHexColor(settings.SecondaryFontColor), stroke);

            PaintFactory.DrawLineWithShadow(canvas, slot.Left, slot.MidY, slot.Right, slot.MidY, linePaint, shadowPaint, RenderConstants.ShadowOffset(height));
        }
    }
}
