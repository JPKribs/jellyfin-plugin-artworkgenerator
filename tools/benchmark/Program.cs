using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ArtworkGenerator.Models;
using Jellyfin.Plugin.ArtworkGenerator.Services;
using Jellyfin.Plugin.ArtworkGenerator.Services.Artwork;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Jellyfin.Plugin.ArtworkGenerator.Benchmark;

/// <summary>
/// Times the frame work behind one Edit Images open, outside a server. The plugin's own frame pool
/// and frame extraction service run unchanged. Only Jellyfin's media encoder is stood in for, by one
/// that runs the same ffmpeg command Jellyfin runs for a video image, behind the same limit on how
/// many run at once. Drawing is left out, since it does not depend on how frames are found.
/// </summary>
public static class Program
{
    private static readonly string[] VideoExtensions = { ".mkv", ".mp4", ".m4v", ".mov", ".avi", ".ts" };

    public static async Task<int> Main(string[] args)
    {
        var options = Options.Parse(args);
        if (options == null)
        {
            Console.Error.WriteLine(Options.Usage);
            return 2;
        }

        var encoder = new FfmpegStandIn(options.Ffmpeg);
        var mediaSources = Substitute.For<IMediaSourceManager>();
        BaseItem.MediaSourceManager = mediaSources;
        StandInForServerServices();

        var items = LoadItems(options, mediaSources);
        if (items.Count == 0)
        {
            Console.Error.WriteLine($"No videos found under {options.Media}. Expected a series/ folder, a movie/ folder, or both.");
            return 2;
        }

        // A build missing SkiaSharp's native library fails every decode, which reads as a fast run
        // that held no frames rather than as an error.
        Console.WriteLine($"SkiaSharp native {SkiaSharp.SkiaSharpVersion.Native}");

        var supportsParallel = FramePoolSupportsParallelism();
        var parallels = supportsParallel ? options.Parallel : new[] { 1 };

        Console.WriteLine($"Plugin: {options.Label}. ffmpeg slots: {encoder.Slots}. Sampling: {options.Sampling}. Reuse samples: {(options.Reuse ? "on" : "off")}. Runs: {options.Runs}.");
        Console.WriteLine();
        Console.WriteLine("| Build | Item | Videos | Choices | Parallel | ffmpeg calls | Peak at once | Frames held | Seconds |");
        Console.WriteLine("| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

        foreach (var (name, videos) in items)
        {
            foreach (var choices in options.Choices)
            {
                foreach (var parallel in parallels)
                {
                    var runs = new List<RunResult>();
                    for (var run = 0; run < options.Runs; run++)
                    {
                        runs.Add(await OpenEditImagesAsync(options, encoder, videos, choices, parallel).ConfigureAwait(false));
                    }

                    var median = runs.OrderBy(r => r.Seconds).ElementAt(runs.Count / 2);
                    Console.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"| {options.Label} | {name} | {videos.Count} | {choices} | {(supportsParallel ? parallel.ToString(CultureInfo.InvariantCulture) : "n/a")} | {median.Calls} | {median.Peak} | {median.Frames} | {median.Seconds:F1} |"));
                }
            }
        }

        return 0;
    }

    // OpenEditImagesAsync
    // One Edit Images open on a fresh pool: every enabled image type acquires the frames it would
    // draw, in the order the provider asks for them, as ArtworkService does. Each type's need is
    // its rank offset plus the choice count.
    private static async Task<RunResult> OpenEditImagesAsync(Options options, FfmpegStandIn encoder, IReadOnlyList<Video> videos, int choices, int parallel)
    {
        var root = Path.Combine(Path.GetTempPath(), "ag-benchmark-" + Guid.NewGuid().ToString("N"));
        var settings = new FrameExtractionSettings
        {
            AvoidMediaSegments = false,
            ReuseSamples = options.Reuse
        };

        ApplySampling(settings, options.Sampling);

        // Builds with Parallel extractions read it from the settings too, for a single video's samples.
        typeof(FrameExtractionSettings).GetProperty("ParallelExtractions")?.SetValue(settings, parallel);

        var extractor = new FrameExtractionService(
            NullLogger<FrameExtractionService>.Instance,
            encoder.Encoder,
            Substitute.For<IMediaSegmentManager>(),
            Substitute.For<ILibraryManager>(),
            () => settings);

        using var pool = CreateFramePool(extractor, root, parallel);
        var key = FramePoolService.KeyFor(Guid.NewGuid(), settings.ExtractWindowStart, settings.ExtractWindowEnd);

        encoder.Reset();
        var clock = Stopwatch.StartNew();
        var frames = 0;

        foreach (var slot in options.Slots)
        {
            var needed = RankOffset(slot, choices, options.Reuse) + choices;
            using var lease = await pool.AcquireAsync(key, () => videos, settings.ExtractWindowStart, settings.ExtractWindowEnd, needed, CancellationToken.None).ConfigureAwait(false);
            frames = lease.Paths.Count;
        }

        clock.Stop();
        pool.Dispose();
        TryDeleteDirectory(root);

        return new RunResult(encoder.Calls, encoder.Peak, frames, clock.Elapsed.TotalSeconds);
    }

    // ApplySampling
    // Builds with the three sampling numbers take them directly, from a preset name or from
    // per-frame/minimum/maximum. Older builds have only the Sampling Rate preset.
    private static void ApplySampling(FrameExtractionSettings settings, string sampling)
    {
        var type = typeof(FrameExtractionSettings);
        var perFrame = type.GetProperty("SamplesPerFrame");
        var parts = sampling.Split('/');

        if (perFrame == null)
        {
            var preset = type.GetProperty("SamplingRate")!;
            var rate = Enum.Parse(typeof(SamplingRate), sampling, ignoreCase: true);
            preset.SetValue(settings, rate);
            return;
        }

        var numbers = parts.Length == 3
            ? parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray()
            : Enum.Parse<SamplingRate>(sampling, ignoreCase: true) switch
            {
                SamplingRate.Low => new[] { 2, 4, 24 },
                SamplingRate.High => new[] { 8, 16, 64 },
                _ => new[] { 4, 8, 48 }
            };

        perFrame.SetValue(settings, numbers[0]);
        type.GetProperty("MinimumSamplesPerVideo")!.SetValue(settings, numbers[1]);
        type.GetProperty("MaximumSamplesPerVideo")!.SetValue(settings, numbers[2]);
    }

    // RankOffset
    // The rank each image type starts at in the picker, mirroring ArtworkService.RankOffset.
    private static int RankOffset(ArtworkSlot slot, int choices, bool reuse)
    {
        if (reuse)
        {
            return slot switch
            {
                ArtworkSlot.Backdrop => 1,
                ArtworkSlot.Thumb => 2,
                ArtworkSlot.Logo => 3,
                _ => 0
            };
        }

        var block = slot switch
        {
            ArtworkSlot.Thumb => 1,
            ArtworkSlot.Logo => 2,
            ArtworkSlot.Backdrop => 3,
            _ => 0
        };

        return block * choices;
    }

    // FramePoolSupportsParallelism
    // Builds before Parallel extractions take no limit, so the benchmark measures them at 1.
    private static bool FramePoolSupportsParallelism()
        => typeof(FramePoolService).GetConstructors().Any(c => c.GetParameters().Length >= 5);

    private static FramePoolService CreateFramePool(FrameExtractionService extractor, string root, int parallel)
    {
        var constructor = typeof(FramePoolService).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var arguments = new List<object?>
        {
            NullLogger<FramePoolService>.Instance,
            extractor,
            root,
            (Func<int?>)(() => 7)
        };

        if (constructor.GetParameters().Length >= 5)
        {
            arguments.Add((Func<int>)(() => parallel));
        }

        return (FramePoolService)constructor.Invoke(arguments.ToArray());
    }

    // StandInForServerServices
    // Jellyfin's items reach the server through static services it sets at startup, such as the
    // recordings manager a video asks whether it is still recording. Every one left unset gets a
    // stand-in that answers with defaults, which is what a plain file on disk would get.
    private static void StandInForServerServices()
    {
        foreach (var type in new[] { typeof(BaseItem), typeof(Video) })
        {
            foreach (var property in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (property.PropertyType.IsInterface && property.CanWrite && property.GetValue(null) == null)
                {
                    property.SetValue(null, Substitute.For(new[] { property.PropertyType }, Array.Empty<object>()));
                }
            }
        }
    }

    // LoadItems
    // A series from every video in series/, and a film from the first video in movie/.
    private static List<(string Name, IReadOnlyList<Video> Videos)> LoadItems(Options options, IMediaSourceManager mediaSources)
    {
        var items = new List<(string, IReadOnlyList<Video>)>();
        var streams = new Dictionary<Guid, MediaStream>();

        List<MediaStream> StreamsOf(Guid id)
            => streams.TryGetValue(id, out var stream) ? new List<MediaStream> { stream } : new List<MediaStream>();

        mediaSources.GetMediaStreams(Arg.Any<MediaStreamQuery>()).Returns(call => StreamsOf(call.Arg<MediaStreamQuery>().ItemId));
        mediaSources.GetMediaStreams(Arg.Any<Guid>()).Returns(call => StreamsOf(call.Arg<Guid>()));

        Video Load(string path)
        {
            var (seconds, width, height) = Probe(options.Ffprobe, path);
            var video = new Video
            {
                Id = Guid.NewGuid(),
                Name = Path.GetFileNameWithoutExtension(path),
                Path = path,
                RunTimeTicks = (long)(seconds * TimeSpan.TicksPerSecond)
            };

            streams[video.Id] = new MediaStream { Type = MediaStreamType.Video, Index = 0, Width = width, Height = height };
            return video;
        }

        if (options.Items.Contains("series"))
        {
            var folder = Path.Combine(options.Media, "series");
            if (Directory.Exists(folder))
            {
                var videos = Directory.GetFiles(folder).Where(IsVideo).OrderBy(p => p, StringComparer.Ordinal).Select(Load).ToList();
                if (videos.Count > 0)
                {
                    items.Add(("Series", videos));
                }
            }
        }

        if (options.Items.Contains("movie"))
        {
            var folder = Path.Combine(options.Media, "movie");
            var file = Directory.Exists(folder) ? Directory.GetFiles(folder).Where(IsVideo).OrderBy(p => p, StringComparer.Ordinal).FirstOrDefault() : null;
            if (file != null)
            {
                items.Add(("Movie", new[] { Load(file) }));
            }
        }

        return items;
    }

    private static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static (double Seconds, int Width, int Height) Probe(string ffprobe, string path)
    {
        var info = new ProcessStartInfo(ffprobe)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        foreach (var arg in new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height:format=duration", "-of", "default=noprint_wrappers=1", path })
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        var values = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());

        double.TryParse(values.GetValueOrDefault("duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds);
        int.TryParse(values.GetValueOrDefault("width"), out var width);
        int.TryParse(values.GetValueOrDefault("height"), out var height);
        return (seconds, width, height);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private readonly record struct RunResult(int Calls, int Peak, int Frames, double Seconds);
}
