# Profiling

How to find and fix slow C# in this plugin. It covers CPU, memory, allocations, locks, ffmpeg calls, and SkiaSharp drawing. Every other language is out of scope.

## When to Profile

Profile only when there is a reason. Never profile to look for something to improve.

Start when one of these is true.

* A user or a test reports that something is slow, uses too much memory, or starves playback.
* A change adds or alters a loop, a frame extraction, or a draw on a path that runs for every item or every choice, such as a refresh, **Edit Images**, the frame pool, or a poster style.
* A number in a test or a refresh grew from one release to the next.

Without one of these, do not profile. Code that has no symptom and no change is not a problem.

## The Hot Paths

| Path | Entry | Cost |
| :--- | :--- | :--- |
| Edit Images | `ArtworkRemoteImageProvider.GetImages` | One render per choice and design, so **Choices in Edit Images** multiplies it |
| Refresh | `ArtworkService.GenerateAsync` | One render for each image a profile turns on, for every item in the library |
| Frame extraction | `FrameExtractionService.ExtractFrameCandidatesAsync` | One ffmpeg seek per sample, so **Samples per frame** multiplies it, and **Parallel extractions** runs that many at once |
| Frame pool | `FramePoolService.AcquireAsync` | Frames on disk shared by every image of one item, behind a gate per item |
| Drawing | The generators in `Services/Posters/Types/` | SkiaSharp work per image, with text fitting in `TextUtils` |
| Choice cache | `GeneratedImageCache` | Rendered choices held in memory, up to `DefaultMaxBytes` |

## The Tiers

Profiling has three tiers. Start at Tier 1. Go up a tier only when the one below could not answer the question. Most issues end at Tier 1 or Tier 2.

| Tier | Answers | Budget | Needs a server |
| :--- | :--- | :--- | :--- |
| 1. Time One Method | Is this code slow by itself | 10 minutes | No |
| 2. Trace One Path | Which code dominates one slow refresh or one **Edit Images** search | 30 minutes | Yes |
| 3. Go Deeper | Why memory grows, or what a second path costs | Ask first | Yes |

* **Tier 1** is a temporary timing test on a bitmap built in the test. It needs no server and no video.
* **Tier 2** is one CPU trace and counters of one path on a test server, the one the issue points at.
* **Tier 3** is a heap dump, a larger library, or a second path. Stop and ask before you start it.

### The Budget

* Each tier has a time budget. When it runs out, stop and report what you have, even when it is inconclusive.
* Profile one path per issue. Never sweep every path "to be sure".
* Stop as soon as the goal is met or the cause is proven.
* Write the tier, the path, and the stop reason in the report.

## The Rule

Measure first, change second, measure again. A change made for speed is not done until a number shows it is faster and the tests still pass.

* Never optimize from a guess. A hot path you cannot show is not a hot path.
* Never trade correctness, a frame quality rule, or a [security](code.md#security) rule for speed.
* Keep the simplest code that meets the goal. Stop when the goal is met.

## Goals

State the goal before you measure. A goal names the operation, the data size, and the limit.

| Good | Bad |
| :--- | :--- |
| **Edit Images** with 10 choices on one episode opens in under 10 seconds | Make Edit Images faster |
| A refresh of a 500 episode series stays under 300 MB | Reduce memory |

The plugin runs inside Jellyfin on the user's server. Treat its CPU, memory, and disk as shared. A refresh that is fast but starves playback has failed.

## Check These First

These causes are common. Check each against the code before you measure anything.

| Check | Usual cause |
| :--- | :--- |
| An ffmpeg call repeated for frames the pool already holds | The frame pool key or lease is not reused |
| A bitmap, paint, font, or typeface built inside a loop | It does not change inside the loop, so build it once and dispose it after |
| A bitmap, surface, or image never disposed | SkiaSharp memory is native, so the GC does not see the pressure |
| Text measured again and again while fitting a title | The fit tries every candidate at full cost |
| A log string built in a hot loop | The string is built even when the level is off |

If the code shows the cause, a Tier 1 test is enough to prove it.

## Tools

Use only the tools Microsoft ships for .NET. They need the .NET 10 SDK, the same as the repo, so check it first.

```bash
dotnet --list-sdks
```

Install the tools once. Update them with `dotnet tool update --global` and the same name.

```bash
dotnet tool install --global dotnet-trace
```

```bash
dotnet tool install --global dotnet-counters
```

```bash
dotnet tool install --global dotnet-gcdump
```

The tools install to `~/.dotnet/tools`. On macOS that folder can be missing from `PATH`, because the dotnet installer writes the literal text `~/.dotnet/tools` to `/etc/paths.d`, and a shell does not expand it. Add this line to `~/.zshrc` and open a new shell.

```bash
export PATH="$PATH:$HOME/.dotnet/tools"
```

| Question | Tool |
| :--- | :--- |
| Which methods use the CPU | `dotnet-trace` |
| Is the process healthy over time | `dotnet-counters` |
| What holds the memory | `dotnet-gcdump` |
| How fast is this one method | A temporary test with `Stopwatch` and `GC.GetAllocatedBytesForCurrentThread()` |

A trace is a `.nettrace` file. Turn it into a speedscope file and open that at [speedscope.app](https://www.speedscope.app), or pass `--format Speedscope` to `collect`.

```bash
dotnet-trace convert run.nettrace --format Speedscope -o run
```

Keep every trace, dump, and test output outside the repository.

## Tier 1: Time One Method

A method with no I/O is best measured alone, such as a generator drawing one poster, the frame scorer, or text fitting.

* Write a temporary xUnit test that builds its input bitmaps, times the method with `Stopwatch`, and reads `GC.GetAllocatedBytesForCurrentThread()` before and after. Write the numbers with `ITestOutputHelper`, and read them with `--logger "console;verbosity=detailed"`. Never commit the test.
* Build and run in `Release`, with `dotnet test -c Release`, and without a debugger.
* Warm up first. Call the method a few times before you measure, so the JIT is not in the number.
* Run it many times and take the median. One run proves nothing.
* Give it realistic input, such as a 1920 by 1080 frame and a long title.

To see where the time goes inside the method, make the test loop for at least 10 seconds, run it, and attach to the test host from another shell.

```bash
dotnet test -c Release --filter FullyQualifiedName~MyTimingTest
```

```bash
pgrep -f testhost
```

```bash
dotnet-trace collect --process-id <pid> --duration 00:00:08 --format Speedscope -o ~/Desktop/method.nettrace
```

### The Frame Benchmark

A change to the frame pool or to frame extraction is timed with the benchmark in [tools/benchmark](../tools/benchmark/README.md) instead of a temporary test. It runs the plugin's frame pool against real video, with a stand-in for Jellyfin's media encoder that runs Jellyfin's own ffmpeg command, and prints the ffmpeg calls and the time for one **Edit Images** open on a baseline commit and on the working tree.

```bash
tools/benchmark/run.sh --media ~/bench-media --choices 6,10,20
```

It lives outside the plugin and the solution, so it never ships and the tests never run it. Keep its media and output outside the repository.

## Tier 2: Trace One Path

The plugin runs in Jellyfin's process, so attach to that process on a test server you run yourself, with the plugin installed from a `Release` build. Never profile a server other people use.

1. Find the Jellyfin process.

    ```bash
    dotnet-trace ps
    ```

2. Start the slow path, such as a refresh of one series or an **Edit Images** search, and record a CPU trace while it runs. Use the `dotnet-sampled-thread-time` profile, since the default one makes a very large file.

    ```bash
    dotnet-trace collect --process-id <pid> --profile dotnet-sampled-thread-time --duration 00:01:00 -o ~/Desktop/run.nettrace
    ```

3. Record counters for the same run. Read allocation, collections, thread pool queue length, and lock contentions.

    ```bash
    dotnet-counters collect --process-id <pid> --counters System.Runtime --format json --duration 00:01:00 -o ~/Desktop/counters
    ```

4. Read the trace. ffmpeg runs as a child process, so its time shows as a wait in `FrameExtractionService`. Count the extractions before you tune one. SkiaSharp work shows as native time under the generator that called it.

## Tier 3: Go Deeper

Stop and ask before you start Tier 3. Say what Tier 2 left unanswered and what you will do.

* Take a heap dump in the middle of the run and after it, and compare them.

    ```bash
    dotnet-gcdump collect --process-id <pid> -o ~/Desktop/mid.gcdump
    ```

* Trace a second path only when the first trace points at it.

## Reading a Trace

* **Inclusive time** is a method plus everything it calls. **Exclusive time** is the method alone. Start from the largest inclusive plugin frame and walk down to the largest exclusive one.
* A frame in `System.Threading` or a wait is time spent blocked. Find what it waits on, since more CPU will not help.
* Many small frames in the allocator or the GC point to allocation pressure. Read the allocation counters, then find the allocating call.

## Common Fixes

Try these in order. Each is cheaper and safer than the next.

1. **Do less.** Draw only the images a profile turns on. Stop scoring once the frames needed are found.
2. **Do it once.** Hoist fonts, paints, and measurements out of loops. Reuse the frame pool for every image of one item.
3. **Do it together.** Run independent work with `ConcurrentTaskRunner` from Base, with a limit that respects the server.
4. **Allocate less.** Dispose every SkiaSharp object, and reuse buffers where a trace shows pressure. Never do this without a number.
5. **Stream, never buffer.** Read a file as a stream. Never load a whole video into memory.

### Async and Threads

* Never block on a task with `.Result` or `.Wait()`. It starves the thread pool.
* Pass the `CancellationToken` through, so a stopped refresh stops working.
* Hold a lock for the shortest time, and never across an `await`.
* Bound every queue and every degree of parallelism.

### Logging

* Use the `ILogger` templates in [code.md](code.md#c-conventions). Never build the message with string interpolation.
* A log in a hot loop costs even when the level is off. Move it out of the loop or guard it with `IsEnabled`.

## Proving the Result

1. Measure the same operation before and after, with the same data and the same machine. Record both numbers.
2. Run the tests for every layer the change touches, per [testing.md](testing.md#layers), and the demo render for a change to drawing. A faster wrong image is a bug.
3. Check that memory stays flat across a long refresh.
4. Report the numbers in the commit message or the report, as before and after for the same operation.

## What Not to Do

* Never ship a profiler, a timing test, a `Stopwatch`, an allocation counter, or a timing log in the plugin. The frame benchmark in `tools/benchmark` is the one timing tool kept in the repository, and only there.
* Never commit trace files, gcdumps, or timing output.
* Never lower **Samples per frame**, the sample minimum or maximum, **Choices in Edit Images**, or a default to hide a slow path.
* Never profile a Debug build. Use `Release`.
* Never change a [stop and ask](../starthere.md#stop-and-ask) item without approval, even when it is the cause.

## Checklist

* [ ] The reason to profile is a symptom, a change to a hot path, or a number that grew.
* [ ] The tier, the path, and the stop reason are in the report.
* [ ] The goal names the operation, the data size, and the limit.
* [ ] A trace, a counter, or a timed test shows where the time or memory goes.
* [ ] The fix is the simplest one that meets the goal.
* [ ] Before and after numbers exist for the same operation and data.
* [ ] A `Release` build was measured.
* [ ] The tests for every touched layer pass, and the demo render looks the same.
* [ ] No profiler code, trace file, timing test, or output is in the diff.
* [ ] Anything on the stop and ask list was approved first.
