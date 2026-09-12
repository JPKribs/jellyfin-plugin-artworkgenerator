using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SkiaSharp;
using Jellyfin.Plugin.ArtworkGenerator.Configuration;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Posters
{
    public interface IPosterGenerator
    {
        // Generate
        // Generates a poster from a provided canvas and episode metadata using layered rendering,
        // returning the encoded JPEG bytes, or null when rendering failed.
        byte[]? Generate(
            SKBitmap canvas,
            ArtworkSubject subject,
            PosterSettings settings);

        // Style
        // The poster style this generator produces.
        PosterStyle Style { get; }

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        string Description { get; }

        // PrimaryDescription
        // One sentence saying what the title is and where this style puts it, shown at the top of
        // the Title section so the settings below it read in context.
        string PrimaryDescription { get; }

        // SecondaryDescription
        // One sentence saying what the subtitle is and where this style puts it, shown at the top
        // of the Subtitle section.
        string SecondaryDescription { get; }

        // SupportedShapes
        // The poster shapes this style can lay out.
        ArtworkShapes SupportedShapes { get; }

        // Supports
        // Returns true when this style can lay out the given shape.
        bool Supports(ArtworkShape shape);

        // SettingRules
        // Which settings this style uses, and which it always draws, so the configuration page can
        // offer exactly the settings that do something.
        IReadOnlyDictionary<string, PosterSettingState> SettingRules { get; }

        // SettingText
        // A style's own wording for a shared setting. Text position means "where the text sits" on
        // most designs and "which border edge the title takes" on a framed one; rather than word it
        // vaguely enough to cover both, a style that reads differently says so here.
        IReadOnlyDictionary<string, SettingText> SettingText { get; }
    }

    public abstract class BasePosterGenerator : IPosterGenerator
    {
        // Logger
        // The style's own logger, for the few styles that report more than a failed render.
        protected ILogger Logger { get; }

        // BasePosterGenerator
        // Every style logs the same way about the same pipeline, so the logger lives here. Styles
        // still pass their own typed logger, which keeps each one's log category its own.
        protected BasePosterGenerator(ILogger logger)
        {
            Logger = logger;
        }

        // Style
        // The poster style this generator produces.
        public abstract PosterStyle Style { get; }

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        public abstract string Description { get; }

        // PrimaryDescription
        // Most styles set the title in a text block at the foot of the image, so that is the
        // default; a style that treats it differently says so.
        public virtual string PrimaryDescription
            => "The title is the item's own name, set at the bottom of the image under the subtitle.";

        // SecondaryDescription
        // What fills the subtitle never changes, only where a style puts it, so the default names
        // the content and the usual place.
        public virtual string SecondaryDescription
            => "The subtitle is an episode's numbers, a season's label, or a film's year, set above the title.";

        // SupportedShapes
        // Every style lays out both shapes unless it says otherwise.
        public virtual ArtworkShapes SupportedShapes => ArtworkShapes.All;

        // SettingRules
        // A style offers the shared settings and none of the style-specific ones unless it says
        // otherwise. Overriding this is how a style adds its own settings, or insists on an element
        // it always draws.
        public virtual IReadOnlyDictionary<string, PosterSettingState> SettingRules => PosterSettingRules.None;

        // SettingText
        // A style uses the settings' own wording unless it says otherwise.
        public virtual IReadOnlyDictionary<string, SettingText> SettingText { get; }
            = new Dictionary<string, SettingText>(StringComparer.Ordinal);

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

        // Supports
        // Returns true when this style can lay out the given shape.
        public bool Supports(ArtworkShape shape)
        {
            var flag = shape == ArtworkShape.Portrait ? ArtworkShapes.Portrait : ArtworkShapes.Landscape;
            return (SupportedShapes & flag) != 0;
        }

        // ShowsPrimary
        // Whether the design draws the primary line and this item has one to put in it.
        protected static bool ShowsPrimary(PosterSettings settings, ArtworkSubject subject)
            => settings != null && settings.ShowPrimary && !string.IsNullOrWhiteSpace(subject?.Primary);

        // ShowsSecondary
        // Whether the design draws the secondary line and this item has one. Its long and short
        // renderings are present or absent together, so one question answers for either.
        protected static bool ShowsSecondary(PosterSettings settings, ArtworkSubject subject)
            => settings != null && settings.ShowSecondary && subject?.Secondary.Length > 0;

        // SizeUnit
        // The length every size setting is a percentage of: the poster's short side. A landscape
        // poster's short side is what these sizes always measured against, so existing designs are
        // unchanged; a portrait poster has a shorter one, so a design's text keeps the same weight
        // relative to the frame instead of overflowing it.
        protected static int SizeUnit(int width, int height) => Math.Max(1, Math.Min(width, height));

        // GetSafeAreaMargin
        // Returns the safe area margin as a percentage of the poster dimensions.
        protected static float GetSafeAreaMargin(PosterSettings settings) => settings.PosterSafeArea / 100f;

        // GetElementSpacing
        // The configured gap between stacked elements, in pixels for this poster's short side.
        // Every style resolves spacing through here so one setting moves them all consistently.
        protected static float GetElementSpacing(PosterSettings settings, float unit)
            => unit * (Math.Max(0f, settings.ElementSpacing) / 100f);

        // Layout block keys shared by the styles that stack text against the bottom edge.
        // How far a line may be shrunk to fit before it is trimmed instead. Below this it stops
        // being readable, and a smaller unreadable line is worse than a shorter readable one.
        private const float MinimumFittedScale = 0.6f;

        protected const string SecondaryBlock = "secondary";
        protected const string SeparatorBlock = "separator";
        protected const string PrimaryBlock = "primary";

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
        // packed against whichever edge the text position asks for. An episode's identity line is its season and
        // episode numbers; a season or series draws its label, such as SEASON 2, instead.
        protected void DrawTextStack(SKCanvas canvas, SKRect area, ArtworkSubject subject, PosterSettings settings, int unit)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var align = ResolveTextAlign(settings);
            using var primaryStyle = CreatePrimaryStyle(settings, unit, align);
            using var secondaryStyle = CreateSecondaryStyle(settings, unit, align);

            var parts = subject.SecondaryParts;
            var label = subject.Secondary;
            var showSecondary = settings.ShowSecondary && (parts.Count > 1 || label.Length > 0);

            // An item can have no title of its own, such as a season named after nothing but its
            // number. Its zone is not reserved, so nothing is left holding empty space.
            var showPrimary = ShowsPrimary(settings, subject);
            var showSeparator = showPrimary && showSecondary;

            var column = new LayoutColumn(area, GetElementSpacing(settings, unit), ResolveTextAnchor(settings))
                .Add(SecondaryBlock, showSecondary ? secondaryStyle.LineBox : 0f)
                .Add(SeparatorBlock, showSeparator ? RenderConstants.SeparatorSlotHeight(unit) : 0f)
                .Add(PrimaryBlock, showPrimary ? primaryStyle.BlockHeight(2) : 0f);

            if (column.TryGetSlot(SecondaryBlock, out var secondarySlot))
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

            if (column.TryGetSlot(SeparatorBlock, out var separatorSlot))
            {
                DrawSeparatorLine(canvas, settings, unit, separatorSlot);
            }

            if (column.TryGetSlot(PrimaryBlock, out var primarySlot))
            {
                DrawPrimaryInSlot(canvas, subject.Primary!, primaryStyle, primarySlot, AlignedX(primarySlot, align), primarySlot.Width * RenderConstants.TextWidthMultiplier, settings.LongTextHandling);
            }
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

        // ApplySafeAreaConstraints
        // Calculates the safe area dimensions and offsets for a given poster size.
        // The margin is the safe area percent of the poster's short side, applied as the same
        // pixel amount on all four sides, so the border is visually even (10% of a
        // 1600x1000 poster is a 100 pixel margin both vertically and horizontally).
        protected static void ApplySafeAreaConstraints(
            int width, int height, PosterSettings settings,
            out float safeWidth, out float safeHeight, out float safeLeft, out float safeTop)
        {
            var marginPixels = SizeUnit(width, height) * GetSafeAreaMargin(settings);
            safeLeft = marginPixels;
            safeTop = marginPixels;
            safeWidth = width - (2 * marginPixels);
            safeHeight = height - (2 * marginPixels);
        }

        // Generate
        // Generates a poster using the 4-layer rendering pipeline and returns the encoded JPEG.
        public byte[]? Generate(SKBitmap canvas, ArtworkSubject subject, PosterSettings settings)
        {
            try
            {
                int width = canvas.Width;
                int height = canvas.Height;

                if (settings.PaletteDerivedColors)
                {
                    settings = ApplyDerivedPalette(settings, canvas);
                }

                var imageInfo = new SKImageInfo(
                    width,
                    height,
                    SKColorType.Rgba8888,
                    SKAlphaType.Premul,
                    SKColorSpace.CreateSrgb());

                using var surface = SKSurface.Create(imageInfo);
                var skCanvas = surface.Canvas;
                skCanvas.Clear(SKColors.Transparent);

                // Layer 1: Canvas (base layer)
                RenderCanvas(skCanvas, canvas, subject, settings, width, height);

                // Layer 2: Overlay (color tinting)
                RenderOverlay(skCanvas, subject, settings, width, height);

                // Layer 3: Graphics (static images/watermarks)
                RenderGraphics(skCanvas, subject, settings, width, height);

                // Layer 4: Typography (text and logos)
                RenderTypography(skCanvas, subject, settings, width, height);

                using var finalImage = surface.Snapshot();
                using var data = finalImage.Encode(SKEncodedImageFormat.Jpeg, RenderConstants.JpegQuality);

                return data?.ToArray();
            }
            catch (Exception ex)
            {
                LogError(ex, subject.EpisodeName);
                return null;
            }
        }

        // ApplyDerivedPalette
        // Returns a render-time copy of the settings whose overlay colors are replaced by the
        // dominant color sampled from the canvas (secondary gets a darkened variant for depth).
        // The configured alpha channels are preserved; a transparent canvas leaves settings unchanged.
        private static PosterSettings ApplyDerivedPalette(PosterSettings settings, SKBitmap canvas)
        {
            // An empty or zero-alpha overlay means "no overlay" — leave it alone. ParseHexColor
            // falls back to opaque white for empty/invalid strings, so deriving from it would
            // turn a deliberately disabled overlay into a fully opaque one.
            if (string.IsNullOrEmpty(settings.OverlayColor))
                return settings;

            var primaryAlpha = ColorUtils.ParseHexColor(settings.OverlayColor).Alpha;
            if (primaryAlpha == 0)
                return settings;

            var dominant = ColorUtils.GetDominantColor(canvas);
            if (dominant == SKColor.Empty)
                return settings;

            var derived = settings.Clone();
            derived.OverlayColor = ColorUtils.ToArgbHex(dominant.WithAlpha(primaryAlpha));

            // The secondary color only participates when it is itself enabled; a zero-alpha
            // secondary keeps its meaning ("fall back to primary") in RenderOverlay.
            var secondaryAlpha = string.IsNullOrEmpty(settings.OverlaySecondaryColor)
                ? (byte)0
                : ColorUtils.ParseHexColor(settings.OverlaySecondaryColor).Alpha;
            if (secondaryAlpha > 0)
            {
                derived.OverlaySecondaryColor = ColorUtils.ToArgbHex(ColorUtils.Darken(dominant, 0.45f).WithAlpha(secondaryAlpha));
            }

            return derived;
        }

        // RenderCanvas
        // Draws the base canvas bitmap onto the surface.
        protected virtual void RenderCanvas(SKCanvas skCanvas, SKBitmap canvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(skCanvas);
            skCanvas.DrawBitmap(canvas, 0, 0);
        }

        // RenderOverlay
        // Applies a color overlay with optional gradient to the poster.
        protected virtual void RenderOverlay(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            if (!TryGetOverlayColor(settings, out var primaryColor))
            {
                return;
            }

            FillOverlay(skCanvas, settings, SKRect.Create(width, height), primaryColor);
        }

        // TryGetOverlayColor
        // The guard every overlay shares: an unset or fully transparent overlay color means the
        // style draws no overlay at all.
        protected static bool TryGetOverlayColor(PosterSettings settings, out SKColor color)
        {
            color = SKColors.Empty;

            if (settings == null || string.IsNullOrEmpty(settings.OverlayColor))
            {
                return false;
            }

            color = ColorUtils.ParseHexColor(settings.OverlayColor);
            return color.Alpha != 0;
        }

        // FillOverlay
        // Paints the overlay across a rectangle, flat or as the configured gradient. Every style
        // that lays down an overlay goes through here, so a style that punches a shape out of one
        // gets the same gradient support as a style that does not.
        protected void FillOverlay(SKCanvas canvas, PosterSettings settings, SKRect rect, SKColor primaryColor)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentNullException.ThrowIfNull(settings);

            if (settings.OverlayGradient == OverlayGradient.None)
            {
                using var flatPaint = new SKPaint
                {
                    Color = primaryColor,
                    Style = SKPaintStyle.Fill
                };
                canvas.DrawRect(rect, flatPaint);
                return;
            }

            var secondaryColor = ColorUtils.ParseHexColor(settings.OverlaySecondaryColor);
            if (secondaryColor.Alpha == 0)
            {
                secondaryColor = primaryColor;
            }

            // SKPaint does not own its shader, so the gradient is disposed here rather than left to
            // the finalizer — a full library run creates one per poster.
            using var gradient = CreateOverlayGradient(settings.OverlayGradient, rect, primaryColor, secondaryColor);
            if (gradient == null)
            {
                return;
            }

            using var gradientPaint = new SKPaint
            {
                Shader = gradient,
                Style = SKPaintStyle.Fill,
                IsDither = true
            };
            canvas.DrawRect(rect, gradientPaint);
        }

        // DrawPunchedOverlay
        // The shape Cutout and Brush share: lay the overlay into its own layer, erase something out
        // of it so the image shows through the hole, then optionally trace the hole's edge. The
        // outline has to be drawn after the layer is restored in some styles and before the punch
        // in others, which is why both moments are offered rather than one.
        protected void DrawPunchedOverlay(
            SKCanvas canvas,
            PosterSettings settings,
            int width,
            int height,
            Action<SKCanvas, SKColor> punch,
            Action<SKCanvas, SKColor>? afterRestore = null)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentNullException.ThrowIfNull(punch);

            if (!TryGetOverlayColor(settings, out var overlayColor))
            {
                return;
            }

            canvas.SaveLayer();
            FillOverlay(canvas, settings, SKRect.Create(width, height), overlayColor);
            punch(canvas, overlayColor);
            canvas.Restore();

            afterRestore?.Invoke(canvas, overlayColor);
        }

        // CreatePunchPaint
        // Erases whatever is drawn with it out of the overlay layer. A blur radius feathers the
        // edge, which reads as paint rather than as a digital cut.
        protected static SKPaint CreatePunchPaint(float blurRadius = 0f)
        {
            var paint = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Fill,
                BlendMode = SKBlendMode.DstOut,
                IsAntialias = true
            };

            if (blurRadius > 0f)
            {
                // SKPaint does not own its mask filter, so it is disposed with the paint below.
                paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurRadius);
            }

            return paint;
        }

        // CreateOutlinePaint
        // The contrasting line traced around a punched hole, shared by every style that offers the
        // cutout border toggle.
        protected static SKPaint CreateOutlinePaint(SKColor overlayColor, float strokeWidth)
        {
            return new SKPaint
            {
                Color = ColorUtils.GetContrastingOutline(overlayColor),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1f, strokeWidth),
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round
            };
        }

        // CenterInSafeArea
        // Puts a band of the given height in the middle of the safe area. A style whose focal
        // element is the composition — lettering cut out of the image, a run of brush strokes —
        // sits it here, so moving the text does not drag the artwork around with it.
        protected static SKRect CenterInSafeArea(SKRect safeArea, float bandHeight)
        {
            var clamped = Math.Min(bandHeight, safeArea.Height);
            return SKRect.Create(safeArea.Left, safeArea.MidY - (clamped / 2f), safeArea.Width, clamped);
        }

        // FocalBandHeight
        // How much room the focal element gets once the text zone is reserved, never letting it
        // fall below the floor a style sets for itself.
        protected static float FocalBandHeight(SKRect safeArea, float remainingHeight, float minimumRatio)
        {
            return Math.Max(remainingHeight, safeArea.Height * minimumRatio);
        }

        // CreateOverlayGradient
        // Creates a shader for the specified gradient direction.
        protected virtual SKShader? CreateOverlayGradient(OverlayGradient gradientType, SKRect rect, SKColor primaryColor, SKColor secondaryColor)
        {
            var colors = new[] { primaryColor, secondaryColor };

            return gradientType switch
            {
                OverlayGradient.LeftToRight => SKShader.CreateLinearGradient(
                    new SKPoint(rect.Left, rect.MidY),
                    new SKPoint(rect.Right, rect.MidY),
                    colors, null, SKShaderTileMode.Clamp, SKMatrix.Identity),

                OverlayGradient.BottomToTop => SKShader.CreateLinearGradient(
                    new SKPoint(rect.MidX, rect.Bottom),
                    new SKPoint(rect.MidX, rect.Top),
                    colors, null, SKShaderTileMode.Clamp, SKMatrix.Identity),

                OverlayGradient.TopLeftCornerToBottomRightCorner => SKShader.CreateLinearGradient(
                    new SKPoint(rect.Left, rect.Top),
                    new SKPoint(rect.Right, rect.Bottom),
                    colors, null, SKShaderTileMode.Clamp, SKMatrix.Identity),

                OverlayGradient.TopRightCornerToBottomLeftCorner => SKShader.CreateLinearGradient(
                    new SKPoint(rect.Right, rect.Top),
                    new SKPoint(rect.Left, rect.Bottom),
                    colors, null, SKShaderTileMode.Clamp, SKMatrix.Identity),

                _ => null
            };
        }

        // RenderGraphics
        // Loads and draws a static graphic image within the safe area.
        protected virtual void RenderGraphics(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            if (string.IsNullOrEmpty(settings.GraphicPath))
                return;

            if (!File.Exists(settings.GraphicPath))
            {
                LogError(new FileNotFoundException("Graphic file not found"), settings.GraphicPath);
                return;
            }

            try
            {
                using var stream = File.OpenRead(settings.GraphicPath);
                using var graphicBitmap = SKBitmap.Decode(stream);
                if (graphicBitmap == null)
                    return;

                ApplySafeAreaConstraints(width, height, settings, out float safeWidth, out float safeHeight, out float safeLeft, out float safeTop);
                var graphicRect = CalculateGraphicRect(graphicBitmap, width, height, safeLeft, safeTop, safeWidth, safeHeight, settings);

                using var graphicPaint = new SKPaint { IsAntialias = true };
                PaintFactory.DrawBitmap(skCanvas, graphicBitmap, graphicRect, graphicPaint);
            }
            catch
            {
                LogError(new InvalidDataException("Failed to load or render graphic"), settings.GraphicPath);
            }
        }

        // RenderTypography
        // Draws text elements on the poster.
        protected abstract void RenderTypography(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height);

        // GetSafeAreaBounds
        // Returns the safe area as an SKRect for the given poster dimensions.
        protected static SKRect GetSafeAreaBounds(int width, int height, PosterSettings settings)
        {
            ApplySafeAreaConstraints(width, height, settings, out float safeWidth, out float safeHeight, out float safeLeft, out float safeTop);
            return new SKRect(safeLeft, safeTop, safeLeft + safeWidth, safeTop + safeHeight);
        }

        // LogError
        // Reports a failed render. The style names itself, so this is written once rather than
        // twelve times with one word changed.
        protected void LogError(Exception ex, string? itemName)
        {
            Logger.LogError(ex, "Failed to generate {Style} artwork for {Item}", Style, itemName);
        }

        // CalculateGraphicRect
        // Calculates the destination rectangle for a graphic while preserving aspect ratio.
        protected virtual SKRect CalculateGraphicRect(SKBitmap graphicBitmap, int posterWidth, int posterHeight, float safeLeft, float safeTop, float safeWidth, float safeHeight, PosterSettings settings)
        {
            ArgumentNullException.ThrowIfNull(graphicBitmap);
            ArgumentNullException.ThrowIfNull(settings);

            // One size, measured from the short side like every other size, and the graphic is
            // fitted inside a box that size. Two independent percentages could stretch it and made
            // the same design look different in each shape.
            var box = SizeUnit(posterWidth, posterHeight) * (Math.Max(1f, settings.GraphicSize) / 100f);
            var originalAspect = (float)graphicBitmap.Width / graphicBitmap.Height;

            float finalWidth = originalAspect >= 1f ? box : box * originalAspect;
            float finalHeight = originalAspect >= 1f ? box / originalAspect : box;

            var x = CalculateGraphicX(settings.GraphicAlignment, safeLeft, safeWidth, finalWidth);
            var y = CalculateGraphicY(settings.GraphicPosition, safeTop, safeHeight, finalHeight);

            return new SKRect(x, y, x + finalWidth, y + finalHeight);
        }

        // CalculateGraphicX
        // Calculates the horizontal position for a graphic based on alignment.
        private float CalculateGraphicX(Alignment alignment, float safeLeft, float safeWidth, float graphicWidth)
        {
            return alignment switch
            {
                Alignment.Left => safeLeft,
                Alignment.Center => safeLeft + (safeWidth - graphicWidth) / 2f,
                Alignment.Right => safeLeft + safeWidth - graphicWidth,
                _ => safeLeft + (safeWidth - graphicWidth) / 2f
            };
        }

        // CalculateGraphicY
        // Calculates the vertical position for a graphic based on position.
        private float CalculateGraphicY(Position position, float safeTop, float safeHeight, float graphicHeight)
        {
            return position switch
            {
                Position.Top => safeTop,
                Position.Center => safeTop + (safeHeight - graphicHeight) / 2f,
                Position.Bottom => safeTop + safeHeight - graphicHeight,
                _ => safeTop + (safeHeight - graphicHeight) / 2f
            };
        }

    }
}
