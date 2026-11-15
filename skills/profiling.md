# Profiling

How to find and fix slow C# in this plugin. It covers CPU, memory, allocations, locks, store files, and HTTP. Every other language is out of scope.

## When to Profile

Profile only when there is a reason. Never profile to look for something to improve.

Start when one of these is true.

* A user or a test reports that something is slow, uses too much memory, or starves playback.
* A change adds or alters a loop, a file write, or a call to another server on a path that runs for every request, such as serving a page, inlining gated assets, a store read or write, or a route call.
* A number in a test or a task run grew from one release to the next.

Without one of these, do not profile. Code that has no symptom and no change is not a problem.

## The Hot Paths

| Path | Runs | What grows |
| :--- | :--- | :--- |
| `PageService.Render` and `InlineGatedAssets` | Every page view | The page size and the number and size of gated assets |
| `StoreService` writes | Every `pageStore.write` and delete | The store's records, since every write rewrites the whole store file |
| `StoreService` reads and retention | Every `pageStore.read`, and the retention task | The store's records |
| `RouteProxy.ForwardAsync` | Every `serverFetch` | The response size, up to its cap |
| `ConfigurationValidator.Validate` | Every configuration save | The number of pages and the size of assets |

## The Tiers

Profiling has three tiers. Start at Tier 1. Go up a tier only when the one below could not answer the question. Most issues end at Tier 1 or Tier 2.

| Tier | Answers | Budget | Needs a server |
| :--- | :--- | :--- | :--- |
| 1. Time One Method | Is this code slow by itself | 10 minutes | No |
| 2. Trace One Route | Which code dominates one slow route or task | 30 minutes | Yes |
| 3. Go Deeper | Why memory grows, or what a second path costs | Ask first | Yes |

* **Tier 1** is a temporary timing test against the method, with realistic input built in the test.
* **Tier 2** is one CPU trace and counters of Jellyfin while one route or task runs on a test server. Pick the one path the issue points at.
* **Tier 3** is a heap dump, a scaled copy of a store file, or a second path. Stop and ask before you start it.

### The Budget

* Each tier has a time budget. When it runs out, stop and report what you have, even when it is inconclusive.
* Profile one path per issue. Never sweep every path "to be sure".
* Stop as soon as the goal is met or the cause is proven.
* Write the tier, the path, and the stop reason in the report.

## The Rule

Measure first, change second, measure again. A change made for speed is not done until a number shows it is faster and the tests still pass.

* Never optimize from a guess. A hot path you cannot show is not a hot path.
* Never trade correctness, a limit, or a [security](code.md#security) rule for speed.
* Keep the simplest code that meets the goal. Stop when the goal is met.

## Goals

State the goal before you measure. A goal names the operation, the data size, and the limit.

| Good | Bad |
| :--- | :--- |
| A write to a store of 10,000 records finishes in under 50 ms | Make stores faster |
| A page with five gated assets of 1 MB renders under 20 MB of allocation | Reduce memory |

The plugin runs inside Jellyfin on the user's server. Treat its CPU, memory, and disk as shared. A route that is fast but starves playback has failed.

## Check These First

These causes are common. Check each against the code before you measure anything.

| Check | Usual cause |
| :--- | :--- |
| A loop that reads the configuration for every page, asset, or record | The answer does not change inside a request, so read it once |
| A store write that measures every record again | The payload size is kept in step with each change, so measure only the record that changed |
| Gated assets decoded or re-encoded for every view | The Base64 is already in the configuration, so copy it as text |
| A route response read whole before the cap is checked | The declared length is checked first, so an oversized answer is refused unread |
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

A trace is a `.nettrace` file. Open it in PerfView on Windows, or turn it into a speedscope file and open that at [speedscope.app](https://www.speedscope.app). Keep every trace, dump, and test output outside the repository.

```bash
dotnet-trace convert run.nettrace --format Speedscope -o run
```

## Tier 1: Time One Method

* Write a temporary xUnit test in the test project that times the method with `Stopwatch` and reads `GC.GetAllocatedBytesForCurrentThread()` before and after. Take the difference on the same thread. Write the numbers with `ITestOutputHelper`, and read them with `--logger "console;verbosity=detailed"`.
* Build and run in `Release`, with `dotnet test -c Release`, and without a debugger.
* Warm up first. Call the method a few times before you measure, so the JIT is not in the number.
* Run it many times and take the median. One run proves nothing.
* Run it before and after the change, on the same machine, with nothing else busy.
* Give it realistic input. A store of three records hides what a store of 10,000 shows.
* Build a large store in a temp folder through `StoreService` itself, never by copying a user's store file.
* Never commit the test, its output, or any trace.

To see where the time goes inside the method, make the test loop for at least 10 seconds, run it in one shell, and attach from another.

```bash
dotnet test -c Release --filter FullyQualifiedName~MyTimingTest
```

```bash
pgrep -f testhost
```

```bash
dotnet-trace collect --process-id <pid> --duration 00:00:08 --format Speedscope -o ~/Desktop/method.nettrace
```

## Tier 2: Trace One Route

The plugin runs in Jellyfin's process, so attach to that process on a test server. Never trace a server that holds real pages or store records.

1. Install a `Release` build on the test server, per [documentation.md](documentation.md#installing-a-build-by-hand), and set up the page or store the issue names.
2. Find Jellyfin's process id with `dotnet-trace ps`.
3. Record a CPU trace while you repeat the slow request, with the `dotnet-sampled-thread-time` profile, which keeps the file small.

    ```bash
    dotnet-trace collect --process-id <pid> --profile dotnet-sampled-thread-time --duration 00:01:00 -o ~/Desktop/run.nettrace
    ```

4. Record counters over the same window. Read allocation, collections, thread pool queue length, and lock contentions.

    ```bash
    dotnet-counters collect --process-id <pid> --counters System.Runtime --format json --duration 00:01:00 -o ~/Desktop/counters
    ```

5. Time a store or page route from the shell. Use an API key from the test server in an `Authorization: MediaBrowser Token="<key>"` header.

    ```bash
    curl -s -o /dev/null -w "%{time_total}" -H 'Authorization: MediaBrowser Token="<key>"' "http://localhost:8096/pages/store/queue/read"
    ```

## Tier 3: Go Deeper

Stop and ask before you start Tier 3. Say what Tier 2 left unanswered and what you will do.

* Take a heap dump in the middle of the run and after it with `dotnet-gcdump collect`, and read both with `dotnet-gcdump report`.
* Scale a copy of a test store file to the size the issue needs, and run against the copy. Never touch a live store file.
* Trace a second path only when the first trace points at it.

## Reading a Trace

* **Inclusive time** is a method plus everything it calls. **Exclusive time** is the method alone. Start from the largest inclusive plugin frame and walk down to the largest exclusive one.
* A frame in `System.Threading` or a wait is time spent blocked. In this plugin that is usually a store's gate, so find who holds it.
* Many small frames in the allocator or the GC point to allocation pressure. Read the allocation counters, then find the allocating call.
* Time spent in a `Task` that never shows CPU is waiting on I/O, such as a route target or a store write.

## Common Fixes

Try these in order. Each is cheaper and safer than the next.

1. **Do less.** Skip work whose input has not changed. Stop a pass at the first answer.
2. **Do it once.** Hoist work out of a loop. Cache a result for the length of one request, never across requests unless it is invalidated.
3. **Do it in bulk.** One file write for a batch of record changes, not one per record.
4. **Allocate less.** Use `Span<T>`, `StringBuilder`, pooled buffers, and `static` lambdas where a trace shows allocation pressure. Never do this without a number.
5. **Stream, never buffer.** Read a large body as a stream, within its cap.

### Store Files

* A store keeps its records in memory and rewrites its whole file on each change. A change to that shape is a [stop and ask](../starthere.md#stop-and-ask) item.
* Hold a store's gate for the shortest time, and never across an `await`.
* Write with `AtomicFile`. Never write in place to save a copy.

### Async and Threads

* Never block on a task with `.Result` or `.Wait()`. It starves the thread pool.
* Pass the `CancellationToken` through, so a stopped request stops working.
* Bound every queue and every degree of parallelism.

### HTTP

* Use the named client `RouteProxy.HttpClientName`. Never build a new `HttpClient` per call.
* Never raise the response cap or the timeout to hide a slow route target.

### Logging

* Use the `ILogger` templates in [code.md](code.md#c-conventions). Never build the message with string interpolation.
* A log in a hot loop costs even when the level is off. Move it out of the loop or guard it with `IsEnabled`.

## Proving the Result

1. Measure the same operation before and after, with the same data and the same machine. Record both numbers.
2. Run the tests for every layer the change touches, per [testing.md](testing.md#layers).
3. Check that memory and the thread count stay flat across a long run.
4. Report the numbers in the commit message or the report, as before and after for the same operation.

## What Not to Do

* Never ship a profiler, a timing test, a `Stopwatch`, an allocation counter, or a timing log in the plugin.
* Never commit trace files, gcdumps, or timing output.
* Never raise a timeout or a cap to hide a slow call.
* Never remove a record, size, or response limit, a check, or a guard to gain speed.
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
* [ ] The tests for every touched layer pass.
* [ ] No profiler code, trace file, timing test, or output is in the diff.
* [ ] Anything on the stop and ask list was approved first.
