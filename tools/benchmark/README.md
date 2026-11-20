# Frame Benchmark

Times the frame work behind one **Edit Images** open, outside a server, and compares a baseline commit against the working tree. Nothing here is part of the plugin build, its zip, or the solution, so `dotnet test` never runs it.

The plugin's own `FramePoolService` and `FrameExtractionService` run unchanged. Only Jellyfin's media encoder is stood in for, by one that runs the ffmpeg command Jellyfin 10.11 runs for a video image, behind the same one-per-core limit. Drawing is left out, since it does not depend on how frames are found. Time it with a Tier 1 test from [profiling.md](../../skills/profiling.md#tier-1-time-one-method) instead.

| File | Does |
| :--- | :--- |
| `Benchmark.csproj` | The console app. `PluginProject` picks the plugin checkout it builds against |
| `Program.cs` | Opens Edit Images on a fresh frame pool for each case, and prints a table |
| `FfmpegStandIn.cs` | The media encoder stand-in, with Jellyfin's ffmpeg arguments and limit |
| `Options.cs` | The command line |
| `run.sh` | Builds the benchmark against a baseline commit and the working tree, and runs both |

## Requirements

* The .NET 10 SDK.
* ffmpeg and ffprobe on `PATH`, or `--ffmpeg` and `--ffprobe`.
* A media folder outside the repository, holding `series/`, where every video is one episode, and `movie/`, where the first video is the film. Real files on the storage you want to measure give the truest numbers.

To make synthetic media instead, ten 4 minute 1080p episodes and one 4K film, run this from the media folder. Noise keeps the bitrate and the decode cost near real video, and a 240 frame GOP is a common encode.

```bash
mkdir -p series movie
```

```bash
for i in $(seq -w 1 10); do ffmpeg -v error -f lavfi -i "testsrc2=s=1920x1080:r=24,noise=alls=12:allf=t+u" -t 240 -c:v libx264 -preset ultrafast -g 240 -pix_fmt yuv420p series/S01E$i.mkv; done
```

```bash
ffmpeg -v error -f lavfi -i "testsrc2=s=3840x2160:r=24,noise=alls=12:allf=t+u" -t 240 -c:v libx264 -preset ultrafast -g 240 -pix_fmt yuv420p movie/Movie.mkv
```

## Run

From the repository root, compare the last commit with the working tree.

```bash
tools/benchmark/run.sh --media ~/bench-media --choices 6,10,20
```

`BASELINE` picks another commit, and `BASELINE=none` measures the working tree alone.

```bash
BASELINE=v12.4.5.0 tools/benchmark/run.sh --media ~/bench-media
```

| Option | Default | Sets |
| :--- | :--- | :--- |
| `--items` | `series,movie` | The items opened |
| `--choices` | `6,10` | **Choices in Edit Images** |
| `--parallel` | `1,2` | **Parallel extractions**. A build without it is measured as `n/a` |
| `--slots` | `primary,thumb,backdrop` | The image types the profile turns on. A color logo takes no frames, so `logo` is off unless asked for |
| `--reuse` | `true` | **Reuse samples** |
| `--sampling` | `Medium` | The sampling: `Low`, `Medium`, or `High` as the old **Sampling Rate** presets, or **Samples per frame**, **Minimum samples per video**, and **Maximum samples per video** as `1/1/64`. Numbers need a build that has them |
| `--runs` | `1` | Opens per case. The median is reported |

## Reading the Table

| Column | Means |
| :--- | :--- |
| ffmpeg calls | Extractions run. Each is one seek and 24 decoded frames, so this drives the time |
| Peak at once | The most extractions running together |
| Frames held | Frames in the pool after the open, which must cover the largest need |
| Seconds | Wall time for the whole open |

Page cache makes a second read of a file faster, so run the cases you compare back to back on the same media, and use `--runs 3` before trusting a small difference. Keep the output outside the repository.
