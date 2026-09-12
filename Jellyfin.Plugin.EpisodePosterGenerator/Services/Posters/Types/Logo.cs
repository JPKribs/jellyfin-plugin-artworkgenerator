using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.EpisodePosterGenerator.Models;
using Jellyfin.Plugin.EpisodePosterGenerator.Utilities;
using SkiaSharp;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EpisodePosterGenerator.Services.Posters
{
    public class LogoPosterGenerator : BasePosterGenerator
    {
        // Style
        // The poster style this generator produces.
        public override PosterStyle Style => PosterStyle.Logo;

        // Description
        // A short, user facing description of this style shown in the configuration UI.
        public override string Description => "Series logo over the image. Puts branding first.";

        // This style places the series logo, so the logo settings are its own.
        public override IReadOnlyDictionary<string, PosterSettingState> SettingRules => PosterSettingRules.Build(
            (PosterSettingRules.LogoPosition, PosterSettingState.Optional),
            (PosterSettingRules.LogoAlignment, PosterSettingState.Optional),
            (PosterSettingRules.LogoHeight, PosterSettingState.Optional));

        // The logo is never squeezed below this share of the safe height, however much text is configured.
        private const float MinimumLogoAreaRatio = 0.2f;

        private readonly ILogger<LogoPosterGenerator> _logger;

        // LogoPosterGenerator
        // Initializes a new instance of the logo poster generator with logging support.
        public LogoPosterGenerator(ILogger<LogoPosterGenerator> logger)
        {
            _logger = logger;
        }

        // RenderGraphics
        // Renders configured graphics and the series logo on the poster.
        protected override void RenderGraphics(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            base.RenderGraphics(skCanvas, subject, settings, width, height);

            RenderSeriesLogo(skCanvas, subject, settings, width, height);
        }

        // RenderTypography
        // Renders the code and headline beneath the logo.
        protected override void RenderTypography(SKCanvas skCanvas, ArtworkSubject subject, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(settings);

            var unit = SizeUnit(width, height);
            var (headline, code) = GetText(subject);

            using var titleStyle = CreateTitleStyle(settings, unit);
            using var episodeStyle = CreateEpisodeStyle(settings, unit);

            var column = BuildColumn(subject, settings, width, height, titleStyle, episodeStyle);

            if (column.TryGetSlot(EpisodeBlock, out var codeSlot))
            {
                episodeStyle.Draw(skCanvas, code, codeSlot.MidX, episodeStyle.BaselineAtBottom(codeSlot));
            }

            if (column.TryGetSlot(TitleBlock, out var titleSlot))
            {
                DrawTitleInSlot(skCanvas, headline, titleStyle, titleSlot, titleSlot.MidX, titleSlot.Width * RenderConstants.TextWidthMultiplier, settings.LongTitleHandling);
            }
        }

        // LogError
        // Logs an error that occurred during logo poster generation.
        protected override void LogError(Exception ex, string? episodeName)
        {
            _logger.LogError(ex, "Failed to generate logo poster for {EpisodeName}", episodeName);
        }

        // GetText
        // The headline and the code line, the same way for every item: its title above its code. A
        // series has no code, so its name sits alone beneath the logo.
        private static (string Headline, string Code) GetText(ArtworkSubject subject)
        {
            return (subject.Title ?? string.Empty, subject.Code);
        }

        // RenderSeriesLogo
        // Renders the series logo image or falls back to text if no logo is available.
        private void RenderSeriesLogo(SKCanvas canvas, ArtworkSubject subject, PosterSettings config, int width, int height)
        {
            var seriesName = subject.SeriesName ?? "Unknown Series";
            var logoPath = GetSeriesLogoPath(subject);
            var logoArea = GetLogoArea(subject, config, width, height);
            var unit = SizeUnit(width, height);

            if (!string.IsNullOrEmpty(logoPath))
            {
                DrawSeriesLogoImage(canvas, logoPath, config.LogoPosition, config.LogoAlignment, config, logoArea, unit);
            }
            else
            {
                DrawSeriesLogoText(canvas, seriesName, config.LogoPosition, config.LogoAlignment, config, logoArea, unit);
            }
        }

        // BuildColumn
        // The single description of this style's vertical layout: the code above a fixed two line
        // headline zone, packed against the bottom of the safe area. Both the typography layer and
        // the logo layer read this same column, so the logo can never be placed from a separately
        // maintained copy of the text's height.
        private static LayoutColumn BuildColumn(ArtworkSubject subject, PosterSettings config, int width, int height, TextStyle titleStyle, TextStyle episodeStyle)
        {
            var safeArea = GetSafeAreaBounds(width, height, config);
            var (headline, code) = GetText(subject);

            var titleHeight = config.ShowTitle && !string.IsNullOrEmpty(headline)
                ? titleStyle.BlockHeight(2)
                : 0f;

            return new LayoutColumn(safeArea, GetElementSpacing(config, SizeUnit(width, height)), LayoutAnchor.Bottom)
                .Add(EpisodeBlock, config.ShowEpisode && code.Length > 0 ? episodeStyle.LineBox : 0f)
                .Add(TitleBlock, titleHeight);
        }

        // GetLogoArea
        // Whatever the text column leaves behind, so Center means "centred in the space actually
        // available" and the graphics layer cannot collide with the typography layer.
        private static SKRect GetLogoArea(ArtworkSubject subject, PosterSettings config, int width, int height)
        {
            var unit = SizeUnit(width, height);
            using var titleStyle = CreateTitleStyle(config, unit);
            using var episodeStyle = CreateEpisodeStyle(config, unit);

            var remaining = BuildColumn(subject, config, width, height, titleStyle, episodeStyle).Remaining;
            var safeArea = GetSafeAreaBounds(width, height, config);

            var minHeight = safeArea.Height * MinimumLogoAreaRatio;
            return remaining.Height >= minHeight
                ? remaining
                : SKRect.Create(safeArea.Left, safeArea.Top, safeArea.Width, minHeight);
        }

        // GetSeriesLogoPath
        // Returns the path to the series logo file if it exists.
        private string? GetSeriesLogoPath(ArtworkSubject subject)
        {
            try
            {
                var path = subject.VideoMetadata?.SeriesLogoFilePath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return path;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking series logo path");
                return null;
            }
        }

        // DrawSeriesLogoImage
        // Draws the series logo image at the specified position and alignment. Its height is a
        // percent of the poster's short side, like every other size setting.
        private void DrawSeriesLogoImage(SKCanvas canvas, string logoPath, Position position, Alignment alignment, PosterSettings config, SKRect logoArea, int unit)
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
                _logger.LogWarning(ex, "Failed to draw series logo image: {Path}", logoPath);
            }
        }

        // DrawSeriesLogoText
        // Draws the series name as text when no logo image is available.
        private static void DrawSeriesLogoText(SKCanvas canvas, string seriesName, Position position, Alignment alignment, PosterSettings config, SKRect logoArea, int unit)
        {
            var fontSize = FontUtils.CalculateFontSizeFromPercentage(config.EpisodeFontSize * RenderConstants.LineHeightMultiplier, unit);
            var typeface = ResolveEpisodeTypeface(config, FontUtils.GetFontStyle(config.EpisodeFontStyle));
            var textAlign = GetSKTextAlign(alignment);

            using var style = PaintFactory.CreateTextStyle(ColorUtils.ParseHexColor(config.EpisodeFontColor), fontSize, typeface, unit, textAlign);

            var availableWidth = logoArea.Width * RenderConstants.TextWidthMultiplier;
            var lines = TextUtils.FitTextToWidth(seriesName, style.Font, availableWidth);

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
