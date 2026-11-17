using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using NSubstitute;

namespace Jellyfin.Plugin.ArtworkGenerator.Benchmark;

/// <summary>
/// Stands in for Jellyfin's media encoder when it extracts one video image at an offset. It runs
/// the command Jellyfin 10.11's MediaEncoder.ExtractImageInternal builds for an SDR, progressive,
/// non-3D video on its first, I-frame attempt:
/// <c>-ss T -i FILE -threads 0 -v quiet -vframes 1 -vf scale=...,thumbnail=n=24 -f image2 OUT</c>.
/// Like Jellyfin, it lets at most one process per core run at once, which is what Jellyfin's
/// <c>ParallelImageEncodingLimit</c> defaults to.
/// </summary>
internal sealed class FfmpegStandIn
{
    private const string Filters = "scale=round(iw*sar/2)*2:round(ih/2)*2,thumbnail=n=24";

    private readonly string _ffmpeg;
    private readonly SemaphoreSlim _slots;
    private readonly string _output = Path.Combine(Path.GetTempPath(), "ag-benchmark-frames");
    private int _calls;
    private int _running;
    private int _peak;

    public FfmpegStandIn(string ffmpeg)
    {
        _ffmpeg = ffmpeg;
        Slots = Environment.ProcessorCount;
        _slots = new SemaphoreSlim(Slots, Slots);
        Directory.CreateDirectory(_output);

        Encoder = Substitute.For<IMediaEncoder>();
        Encoder.ExtractVideoImage(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<MediaSourceInfo>(),
                Arg.Any<MediaStream>(),
                Arg.Any<Video3DFormat?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>())
            .Returns(call => ExtractAsync(call.ArgAt<string>(0), call.ArgAt<TimeSpan?>(5), call.ArgAt<CancellationToken>(6)));
    }

    public IMediaEncoder Encoder { get; }

    public int Slots { get; }

    public int Calls => Volatile.Read(ref _calls);

    public int Peak => Volatile.Read(ref _peak);

    public void Reset()
    {
        _calls = 0;
        _peak = 0;
    }

    private async Task<string> ExtractAsync(string input, TimeSpan? offset, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_output, Guid.NewGuid().ToString("N") + ".jpg");
        var info = new ProcessStartInfo(_ffmpeg) { UseShellExecute = false, CreateNoWindow = true };

        if (offset.HasValue)
        {
            info.ArgumentList.Add("-ss");
            info.ArgumentList.Add(offset.Value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        }

        foreach (var arg in new[] { "-i", input, "-threads", "0", "-v", "quiet", "-vframes", "1", "-vf", Filters, "-f", "image2", path })
        {
            info.ArgumentList.Add(arg);
        }

        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Interlocked.Increment(ref _calls);
            var running = Interlocked.Increment(ref _running);
            InterlockedMax(ref _peak, running);

            using var process = Process.Start(info)!;
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _running);
            _slots.Release();
        }

        return path;
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current)
            {
                return;
            }
        }
    }
}
