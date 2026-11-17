using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.ArtworkGenerator.Models;

namespace Jellyfin.Plugin.ArtworkGenerator.Benchmark;

/// <summary>The command line.</summary>
internal sealed class Options
{
    public const string Usage = """
        Usage: frame-benchmark --media <folder> [options]

          --media <folder>       Holds series/ (every video is an episode) and movie/ (the first video is the film).
          --items series,movie   Which items to open. Default both.
          --choices 6,10         Choices In Edit Images to try. Default 6,10.
          --parallel 1,2         Parallel Extractions to try. Ignored by builds without it. Default 1,2.
          --slots primary,thumb,backdrop
                                 Image types the profile turns on, among primary, thumb, logo, backdrop.
                                 A color logo takes no frames, so logo is left out by default.
          --reuse true|false     Reuse Samples. Default true.
          --sampling Medium      Low, Medium, or High, or per-frame/minimum/maximum such as 1/1/64.
                                 Numbers need a build with the three sampling settings. Default Medium.
          --runs 1               Opens per case, reporting the median. Default 1.
          --label <text>         Names the build in the table. Default "build".
          --ffmpeg <path>        Default ffmpeg on PATH.
          --ffprobe <path>       Default ffprobe on PATH.
        """;

    public string Media { get; private set; } = string.Empty;

    public HashSet<string> Items { get; private set; } = new(StringComparer.OrdinalIgnoreCase) { "series", "movie" };

    public int[] Choices { get; private set; } = { 6, 10 };

    public int[] Parallel { get; private set; } = { 1, 2 };

    public ArtworkSlot[] Slots { get; private set; } = { ArtworkSlot.Primary, ArtworkSlot.Thumb, ArtworkSlot.Backdrop };

    public bool Reuse { get; private set; } = true;

    public string Sampling { get; private set; } = "Medium";

    public int Runs { get; private set; } = 1;

    public string Label { get; private set; } = "build";

    public string Ffmpeg { get; private set; } = "ffmpeg";

    public string Ffprobe { get; private set; } = "ffprobe";

    public static Options? Parse(string[] args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            var value = i + 1 < args.Length ? args[i + 1] : null;
            if (value == null)
            {
                return null;
            }

            switch (args[i])
            {
                case "--media": options.Media = value; break;
                case "--items": options.Items = new HashSet<string>(List(value), StringComparer.OrdinalIgnoreCase); break;
                case "--choices": options.Choices = List(value).Select(int.Parse).ToArray(); break;
                case "--parallel": options.Parallel = List(value).Select(int.Parse).ToArray(); break;
                case "--slots": options.Slots = List(value).Select(s => Enum.Parse<ArtworkSlot>(s, ignoreCase: true)).ToArray(); break;
                case "--reuse": options.Reuse = bool.Parse(value); break;
                case "--sampling": options.Sampling = value; break;
                case "--runs": options.Runs = Math.Max(1, int.Parse(value, CultureInfo.InvariantCulture)); break;
                case "--label": options.Label = value; break;
                case "--ffmpeg": options.Ffmpeg = value; break;
                case "--ffprobe": options.Ffprobe = value; break;
                default: return null;
            }

            i++;
        }

        // The slots run in the order the provider asks for them.
        options.Slots = options.Slots.Distinct().OrderBy(s => Array.IndexOf(Order, s)).ToArray();
        return string.IsNullOrEmpty(options.Media) ? null : options;
    }

    private static readonly ArtworkSlot[] Order = { ArtworkSlot.Primary, ArtworkSlot.Thumb, ArtworkSlot.Logo, ArtworkSlot.Backdrop };

    private static IEnumerable<string> List(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
