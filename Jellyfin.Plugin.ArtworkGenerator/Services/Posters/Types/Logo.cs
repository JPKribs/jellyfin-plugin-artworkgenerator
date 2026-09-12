using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using SkiaSharp;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtworkGenerator.Services.Posters
{
    public class LogoPosterGenerator : BasePosterGenerator
    {
        // Style
        // The poster style this generator produces.
        public override PosterStyle Style => PosterStyle.Logo;

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        public override string Description => "The item's logo over the image. Puts branding first.";

        // PrimaryDescription
        // One sentence on what the title is and where this style puts it.
        public override string PrimaryDescription
            => "The title is the item's own name, set at the bottom under the logo.";

        // SecondaryDescription
        // One sentence on what the subtitle is and where this style puts it.
        public override string SecondaryDescription
            => "The subtitle is the episode code, set on the line above the title.";

        // This style places the logo, so the logo settings are its own.
        public override IReadOnlyDictionary<string, PosterSettingState> SettingRules => PosterSettingRules.Build(
            (PosterSettingRules.LogoPosition, PosterSettingState.Optional),
            (PosterSettingRules.LogoAlignment, PosterSettingState.Optional),
            (PosterSettingRules.LogoHeight, PosterSettingState.Optional));

        // The logo is never squeezed below this share of the safe height, however much text is configured.
        private const float MinimumLogoAreaRatio = 0.2f;

        // LogoPosterGenerator
        // Initializes a new instance of the logo poster generator with logging support.
        public LogoPosterGenerator(ILogger<LogoPosterGenerator> logger)
            : base(logger)
        {
        }

        // RenderGraphics
        // Renders configured graphics and the logo on the poster.
        protected override void RenderGraphics(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            base.RenderGraphics(skCanvas, subject, settings, width, height);

            RenderLogo(skCanvas, subject, settings, width, height);
        }

        // RenderTypography
        // Renders the code and headline beneath the logo.
        protected override void RenderTypography(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var unit = SizeUnit(width, height);
            var (headline, code) = GetText(subject);

            var align = ResolveTextAlign(settings);
            using var primaryStyle = CreatePrimaryStyle(settings, unit, align);
            using var secondaryStyle = CreateSecondaryStyle(settings, unit, align);

            var column = BuildColumn(subject, settings, width, height, primaryStyle, secondaryStyle);

            if (column.TryGetSlot(SecondaryBlock, out var codeSlot))
            {
                DrawFittedLine(skCanvas, secondaryStyle, code, AlignedX(codeSlot, align), secondaryStyle.BaselineAtBottom(codeSlot), codeSlot.Width * RenderConstants.TextWidthMultiplier, settings.LongSubtitleHandling);
            }

            if (column.TryGetSlot(PrimaryBlock, out var primarySlot))
            {
                DrawPrimaryInSlot(skCanvas, headline, primaryStyle, primarySlot, AlignedX(primarySlot, align), primarySlot.Width * RenderConstants.TextWidthMultiplier, settings.LongTextHandling);
            }
        }


        // GetText
        // The headline and the code line, the same way for every item: its title above its code. A
        // series has no code, so its name sits alone beneath the logo.
        private static (string Headline, string Code) GetText(ArtworkSubject subject)
        {
            return (subject.Primary ?? string.Empty, subject.SecondaryShort);
        }

        // RenderLogo
        // Renders the item's logo image, or its name as text when it has no logo.
        private void RenderLogo(SKCanvas canvas, ArtworkSubject subject, PosterSettings config, int width, int height)
        {
            var name = subject.SeriesName ?? "Unknown Series";
            var logoPath = GetLogoPath(subject);
            var logoArea = GetLogoArea(subject, config, width, height);
            var unit = SizeUnit(width, height);

            if (!string.IsNullOrEmpty(logoPath))
            {
                DrawLogoImage(canvas, logoPath, config.LogoPosition, config.LogoAlignment, config, logoArea, unit);
            }
            else
            {
                DrawLogoText(canvas, name, config.LogoPosition, config.LogoAlignment, config, logoArea, unit);
            }
        }

        // BuildColumn
        // The single description of this style's vertical layout: the code above a fixed two line
        // headline zone, packed against the bottom of the safe area. Both the typography layer and
        // the logo layer read this same column, so the logo can never be placed from a separately
        // maintained copy of the text's height.
        private LayoutColumn BuildColumn(ArtworkSubject subject, PosterSettings config, int width, int height, TextStyle primaryStyle, TextStyle secondaryStyle)
        {
            var safeArea = GetSafeAreaBounds(width, height, config);
            var (headline, code) = GetText(subject);

            var primaryHeight = config.ShowPrimary && !string.IsNullOrEmpty(headline)
                ? primaryStyle.BlockHeight(2)
                : 0f;

            return new LayoutColumn(safeArea, GetElementSpacing(config, SizeUnit(width, height)), ResolveTextAnchor(config))
                .Add(SecondaryBlock, ShowsSecondary(config, subject) && code.Length > 0 ? secondaryStyle.LineBox : 0f)
                .Add(PrimaryBlock, primaryHeight);
        }

        // GetLogoArea
        // Whatever the text column leaves behind, so Center means "centered in the space actually
        // available" and the graphics layer cannot collide with the typography layer.
        private SKRect GetLogoArea(ArtworkSubject subject, PosterSettings config, int width, int height)
        {
            var unit = SizeUnit(width, height);
            using var primaryStyle = CreatePrimaryStyle(config, unit);
            using var secondaryStyle = CreateSecondaryStyle(config, unit);

            var remaining = BuildColumn(subject, config, width, height, primaryStyle, secondaryStyle).Remaining;
            var safeArea = GetSafeAreaBounds(width, height, config);

            // Same floor every focal element gets: the text zone may shrink this, never erase it.
            var floor = FocalBandHeight(safeArea, remaining.Height, MinimumLogoAreaRatio);
            return remaining.Height >= floor
                ? remaining
                : SKRect.Create(safeArea.Left, safeArea.Top, safeArea.Width, floor);
        }

        // GetLogoPath
        // Returns the path to the logo selected for the item, its season, or its series, if the
        // file exists.
        private string? GetLogoPath(ArtworkSubject subject)
        {
            try
            {
                var path = subject.VideoMetadata?.LogoFilePath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return path;
                }

                return null;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error checking logo path");
                return null;
            }
        }

        // DrawLogoImage
        // Draws the logo image at the specified position and alignment. Its height is a
        // percent of the poster's short side, like every other size setting.
        private void DrawLogoImage(SKCanvas canvas, string logoPath, Position position, Alignment alignment, PosterSettings config, SKRect logoArea, int unit)
        {
            try
            {
                using var stream = File.OpenRead(logoPath);
                using var bitmap = SKBitmap.Decode(stream);
                if (bitmap == null)
                {
                    return;
                }

                var logoHeight = unit * (config.LogoHeight / 100f);
                var aspect = (float)bitmap.Width / bitmap.Height;
                var logoWidth = logoHeight * aspect;

                if (logoWidth > logoArea.Width)
                {
                    logoWidth = logoArea.Width;
                    logoHeight = logoWidth / aspect;
                }

                // A logo tall enough to reach the text strip is scaled down to fit the space left
                // for it rather than being allowed to overlap.
                if (logoHeight > logoArea.Height)
                {
                    logoHeight = logoArea.Height;
                    logoWidth = logoHeight * aspect;
                }

                var x = CalculateLogoX(alignment, logoArea, logoWidth);
                var y = CalculateLogoY(position, logoArea, logoHeight);
                var rect = new SKRect(x, y, x + logoWidth, y + logoHeight);

                using var paint = new SKPaint { IsAntialias = true };
                PaintFactory.DrawBitmap(canvas, bitmap, rect, paint);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to draw logo image: {Path}", logoPath);
            }
        }

        // DrawLogoText
        // Draws the name as text when no logo image is available.
        private static void DrawLogoText(SKCanvas canvas, string name, Position position, Alignment alignment, PosterSettings config, SKRect logoArea, int unit)
        {
            var fontSize = FontUtils.CalculateFontSizeFromPercentage(config.SecondaryFontSize * RenderConstants.LineHeightMultiplier, unit);
            var typeface = ResolveSecondaryTypeface(config);
            var textAlign = GetSKTextAlign(alignment);

            using var style = PaintFactory.CreateTextStyle(ColorUtils.ParseHexColor(config.SecondaryFontColor), fontSize, typeface, unit, textAlign);

            var availableWidth = logoArea.Width * RenderConstants.TextWidthMultiplier;
            var lines = TextUtils.FitTextToWidth(name, style.Font, availableWidth);

            var x = CalculateLogoX(alignment, logoArea, 0);
            var y = CalculateLogoY(position, logoArea, style.BlockHeight(lines.Count));

            style.DrawLines(canvas, lines, x, y + style.Ascent);
        }

        // CalculateLogoX
        // Calculates the horizontal position for the logo based on alignment.
        private static float CalculateLogoX(Alignment alignment, SKRect safeArea, float logoWidth) => alignment switch
        {
            Alignment.Left => safeArea.Left,
            Alignment.Center => safeArea.Left + (safeArea.Width - logoWidth) / 2f,
            Alignment.Right => safeArea.Right - logoWidth,
            _ => safeArea.Left + (safeArea.Width - logoWidth) / 2f
        };

        // CalculateLogoY
        // Calculates the vertical position for the logo based on position.
        private static float CalculateLogoY(Position position, SKRect safeArea, float logoHeight) => position switch
        {
            Position.Top => safeArea.Top,
            Position.Center => safeArea.Top + (safeArea.Height - logoHeight) / 2f,
            Position.Bottom => safeArea.Bottom - logoHeight,
            _ => safeArea.Top + (safeArea.Height - logoHeight) / 2f
        };

        // GetSKTextAlign
        // Converts an Alignment enum value to the corresponding SKTextAlign.
        private static SKTextAlign GetSKTextAlign(Alignment alignment) => alignment switch
        {
            Alignment.Left => SKTextAlign.Left,
            Alignment.Center => SKTextAlign.Center,
            Alignment.Right => SKTextAlign.Right,
            _ => SKTextAlign.Center
        };
    }
}
