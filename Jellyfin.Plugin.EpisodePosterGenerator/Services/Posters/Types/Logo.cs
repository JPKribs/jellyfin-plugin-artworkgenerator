using System;
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
        public override string Description => "Series logo over the episode image. Puts branding first.";

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
        protected override void RenderGraphics(SKCanvas skCanvas, EpisodeMetadata episodeMetadata, PosterSettings settings, int width, int height)
        {
            base.RenderGraphics(skCanvas, episodeMetadata, settings, width, height);

            RenderSeriesLogo(skCanvas, episodeMetadata, settings, width, height);
        }

        // RenderTypography
        // Renders episode title and code text on the poster.
        protected override void RenderTypography(SKCanvas skCanvas, EpisodeMetadata episodeMetadata, PosterSettings settings, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(episodeMetadata);
            ArgumentNullException.ThrowIfNull(settings);

            using var titleStyle = CreateTitleStyle(settings, height);
            using var episodeStyle = CreateEpisodeStyle(settings, height);

            var column = BuildColumn(episodeMetadata, settings, width, height, titleStyle, episodeStyle);

            if (column.TryGetSlot(EpisodeBlock, out var codeSlot))
            {
                var code = EpisodeCodeUtils.FormatEpisodeCode(episodeMetadata.SeasonNumber ?? 0, episodeMetadata.EpisodeNumberStart ?? 0);
                episodeStyle.Draw(skCanvas, code, codeSlot.MidX, episodeStyle.BaselineAtBottom(codeSlot));
            }

            if (column.TryGetSlot(TitleBlock, out var titleSlot))
            {
                DrawTitleInSlot(skCanvas, episodeMetadata.EpisodeName!, titleStyle, titleSlot, titleSlot.MidX, titleSlot.Width * RenderConstants.TextWidthMultiplier, settings.LongTitleHandling);
            }
        }

        // LogError
        // Logs an error that occurred during logo poster generation.
        protected override void LogError(Exception ex, string? episodeName)
        {
            _logger.LogError(ex, "Failed to generate logo poster for {EpisodeName}", episodeName);
        }

        // RenderSeriesLogo
        // Renders the series logo image or falls back to text if no logo is available.
        private void RenderSeriesLogo(SKCanvas canvas, EpisodeMetadata episodeMetadata, PosterSettings config, int width, int height)
        {
            var seriesName = episodeMetadata.SeriesName ?? "Unknown Series";
            var logoPath = GetSeriesLogoPath(episodeMetadata);
            var logoArea = GetLogoArea(episodeMetadata, config, width, height);

            if (!string.IsNullOrEmpty(logoPath))
                DrawSeriesLogoImage(canvas, logoPath, config.LogoPosition, config.LogoAlignment, config, logoArea, height);
            else
                DrawSeriesLogoText(canvas, seriesName, config.LogoPosition, config.LogoAlignment, config, logoArea, height);
        }

        // BuildColumn
        // The single description of this style's vertical layout: episode code above a fixed two
        // line title zone, packed against the bottom of the safe area.
        //
        // Both the typography layer and the logo layer read this same column, so the logo can no
        // longer be placed from a second, separately maintained copy of the text's height — which
        // is what let a tall centred logo run straight through the episode code.
        private static LayoutColumn BuildColumn(EpisodeMetadata episodeMetadata, PosterSettings config, int width, int height, TextStyle titleStyle, TextStyle episodeStyle)
        {
            var safeArea = GetSafeAreaBounds(width, height, config);

            var titleHeight = config.ShowTitle && !string.IsNullOrEmpty(episodeMetadata.EpisodeName)
                ? titleStyle.BlockHeight(2)
                : 0f;

            return new LayoutColumn(safeArea, GetElementSpacing(config, height), LayoutAnchor.Bottom)
                .Add(EpisodeBlock, config.ShowEpisode ? episodeStyle.LineBox : 0f)
                .Add(TitleBlock, titleHeight);
        }

        // GetLogoArea
        // Whatever the text column leaves behind, so Center means "centred in the space actually
        // available" and the graphics layer cannot collide with the typography layer.
        private static SKRect GetLogoArea(EpisodeMetadata episodeMetadata, PosterSettings config, int width, int height)
        {
            using var titleStyle = CreateTitleStyle(config, height);
            using var episodeStyle = CreateEpisodeStyle(config, height);

            var remaining = BuildColumn(episodeMetadata, config, width, height, titleStyle, episodeStyle).Remaining;
            var safeArea = GetSafeAreaBounds(width, height, config);

            var minHeight = safeArea.Height * MinimumLogoAreaRatio;
            return remaining.Height >= minHeight
                ? remaining
                : SKRect.Create(safeArea.Left, safeArea.Top, safeArea.Width, minHeight);
        }

        // GetSeriesLogoPath
        // Returns the path to the series logo file if it exists.
        private string? GetSeriesLogoPath(EpisodeMetadata episodeMetadata)
        {
            try
            {
                var path = episodeMetadata.VideoMetadata?.SeriesLogoFilePath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking series logo path");
                return null;
            }
        }

        // DrawSeriesLogoImage
        // Draws the series logo image at the specified position and alignment.
        private void DrawSeriesLogoImage(SKCanvas canvas, string logoPath, Position position, Alignment alignment, PosterSettings config, SKRect logoArea, int height)
        {
            try
            {
                using var stream = File.OpenRead(logoPath);
                using var bitmap = SKBitmap.Decode(stream);
                if (bitmap == null) return;

                var logoHeight = height * (config.LogoHeight / 100f);
                var aspect = (float)bitmap.Width / bitmap.Height;
                var logoWidth = logoHeight * aspect;

                if (logoWidth > logoArea.Width)
                {
                    logoWidth = logoArea.Width;
                    logoHeight = logoWidth / aspect;
                }

                // A logo height large enough to reach the text strip is scaled down to fit the
                // space left for it rather than being allowed to overlap.
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
        private static void DrawSeriesLogoText(SKCanvas canvas, string seriesName, Position position, Alignment alignment, PosterSettings config, SKRect logoArea, int height)
        {
            var fontSize = FontUtils.CalculateFontSizeFromPercentage(config.EpisodeFontSize * RenderConstants.LineHeightMultiplier, height);
            var typeface = ResolveEpisodeTypeface(config, FontUtils.GetFontStyle(config.EpisodeFontStyle));
            var textAlign = GetSKTextAlign(alignment);

            using var style = PaintFactory.CreateTextStyle(ColorUtils.ParseHexColor(config.EpisodeFontColor), fontSize, typeface, height, textAlign);

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
