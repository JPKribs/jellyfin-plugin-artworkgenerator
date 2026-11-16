using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Services.Fonts;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// The bundled fonts must all load from the plugin's own DLL, in every style each family ships, so a server with no fonts of its own still draws good text.
/// </summary>
public class BundledFontsTests
{
    /// <summary>
    /// The families the plugin bundles, and the styles each ships.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> Expected = new Dictionary<string, string[]>
    {
        ["Anton"] = new[] { "Regular" },
        ["Bebas Neue"] = new[] { "Regular" },
        ["Lora"] = new[] { "Regular", "Italic", "Bold", "Bold Italic" },
        ["Montserrat"] = new[] { "Regular", "Italic", "Bold", "Bold Italic" },
        ["Noto Sans"] = new[] { "Regular", "Italic", "Bold", "Bold Italic" },
        ["Oswald"] = new[] { "Regular", "Bold" },
        ["Playfair Display"] = new[] { "Regular", "Italic", "Bold", "Bold Italic" },
        ["Poppins"] = new[] { "Regular", "Italic", "Bold", "Bold Italic" },
        ["Roboto"] = new[] { "Regular", "Italic", "Bold", "Bold Italic" },
    };

    /// <summary>
    /// Every family name comes from the font files, and the list is exactly the nine bundled families.
    /// </summary>
    [Fact]
    public void TheNineFamiliesAreBundled()
    {
        Assert.Equal(Expected.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase), BundledFonts.Families);
    }

    /// <summary>
    /// Each family loads in every style it ships, and in no other, so the Settings tab and the docs name the right styles.
    /// </summary>
    [Theory]
    [MemberData(nameof(Families))]
    public void EachFamilyLoadsInEveryStyleItShips(string family)
    {
        var styles = BundledFonts.Faces.Where(f => f.Family == family).Select(f => f.Style).OrderBy(s => s).ToArray();

        Assert.Equal(Expected[family].OrderBy(s => s), styles);
    }

    /// <summary>
    /// A bundled face draws real glyphs, not the empty boxes of a face Skia could not read.
    /// </summary>
    [Theory]
    [MemberData(nameof(Families))]
    public void EachFaceDrawsGlyphs(string family)
    {
        foreach (var face in BundledFonts.Faces.Where(f => f.Family == family))
        {
            Assert.True(Ink(face.Typeface) > 0, family + " " + face.Style + " drew nothing.");
            Assert.NotEqual(0, face.Typeface.GetGlyph('A'));
        }
    }

    /// <summary>
    /// Bold must look bolder than Regular. A Bold that is only a renamed Regular would leave every bold title thin.
    /// </summary>
    [Theory]
    [MemberData(nameof(BoldFamilies))]
    public void BoldDrawsMoreInkThanRegular(string family)
    {
        var regular = BundledFonts.Faces.Single(f => f.Family == family && f.Style == "Regular");
        var bold = BundledFonts.Faces.Single(f => f.Family == family && f.Style == "Bold");

        Assert.True(bold.Weight > regular.Weight);
        Assert.True(Ink(bold.Typeface) > Ink(regular.Typeface) * 1.1, family + " Bold is not bolder than Regular.");
    }

    /// <summary>
    /// The license must ship with every bundled family, as the SIL Open Font License asks.
    /// </summary>
    [Fact]
    public void EveryFamilyFolderCarriesItsLicense()
    {
        var assembly = typeof(BundledFonts).Assembly;
        var folders = BundledFonts.Faces.Select(f => f.FileName[..f.FileName.IndexOf('.', StringComparison.Ordinal)]).Distinct().ToList();

        foreach (var folder in folders)
        {
            using var stream = assembly.GetManifestResourceStream("Jellyfin.Plugin.ArtworkGenerator.Fonts." + folder + ".OFL.txt");
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream!);
            Assert.Contains("SIL Open Font License", reader.ReadToEnd(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Every bundled family name, for the theories above.
    /// </summary>
    public static TheoryData<string> Families()
    {
        var data = new TheoryData<string>();
        foreach (var family in Expected.Keys)
        {
            data.Add(family);
        }

        return data;
    }

    /// <summary>
    /// The bundled families that ship a Bold.
    /// </summary>
    public static TheoryData<string> BoldFamilies()
    {
        var data = new TheoryData<string>();
        foreach (var family in Expected.Where(e => e.Value.Contains("Bold")).Select(e => e.Key))
        {
            data.Add(family);
        }

        return data;
    }

    /// <summary>
    /// Counts the pixels a sample line covers, a measure of how heavy the face is.
    /// </summary>
    internal static int Ink(SKTypeface typeface)
    {
        using var bitmap = new SKBitmap(600, 120);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        using var font = new SKFont(typeface, 64f);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = false };
        canvas.DrawText("Hamburgefonts", 10f, 90f, font, paint);

        var ink = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Red > 128)
                {
                    ink++;
                }
            }
        }

        return ink;
    }
}
