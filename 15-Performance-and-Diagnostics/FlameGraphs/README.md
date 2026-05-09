# Flame graphs

Brendan Gregg's flame graph format. Each box is a function; the width is *inclusive sample count*; the y-axis is the call stack. The flame shape's *width* tells you where time goes.

## Reading them

- **Wide flat plateaus near the top** = leaf functions that take a lot of time. Optimise these first.
- **Tall narrow towers** = deep call stacks; not necessarily slow.
- **Pyramid-shaped clusters** = a small number of paths dominate.
- **Forest of equally-narrow towers** = work is spread thin; harder to win by single-method optimisation.

## How to make one for .NET

```bash
# Linux / macOS / Windows
dotnet-trace collect --process-id <pid> --duration 00:00:30
dotnet-trace convert <file>.nettrace --format Speedscope
# upload <file>.speedscope.json to https://www.speedscope.app
```

Speedscope has three views:

| View | What it shows |
|---|---|
| **Time order** | events on the actual timeline (per-thread) |
| **Left-heavy** | flame graph aggregated, biggest first |
| **Sandwich** | choose a function; see its callers and callees |

Left-heavy is the classic "flame graph" view. Sandwich is fantastic for "I know `ParseHeader` is hot — show me from where".

## Concurrency-specific patterns

- **Thread-time flame graph** (wall time, including blocked time) tells you *where threads are stuck*. Stacks with `Monitor.Enter`/`Wait`/`SemaphoreSlim.Wait` near the top → contention.
- **CPU flame graph** (sampled actively-running stacks) tells you *where CPU goes*. Stacks dominated by your business logic → great. Stacks dominated by GC or marshalling → optimise.

In PerfView, the equivalent of left-heavy flame is the "Stacks" view sorted by Inc. The PerfView view is denser and harder for newcomers; speedscope is friendlier.

## Common surprises in .NET flame graphs

1. **`AsyncMethodBuilder.AwaitOnCompleted` near the top** — async machinery overhead. Indicates many short-lived awaits; consider batching or `ValueTask`.
2. **`ExecutionContext.Run`** — every continuation crosses this. Can be reduced with `ThreadPool.UnsafeQueueUserWorkItem` for hot paths.
3. **`String.Concat`/`StringBuilder.ToString`** — the .NET version of "you're allocating too much".
4. **`Monitor.Enter` + `Monitor.Exit`** in non-trivial proportions — lock contention. Measure with `[ThreadingDiagnoser]` benchmarks to confirm.

## When NOT a flame graph

For latency analysis (what made this *one* request slow?), use distributed tracing (OpenTelemetry, Honeycomb, Jaeger). Flame graphs are *aggregate* — they tell you what's slow on average, not what's slow for the request that just timed out.

## Tools

- **PerfView** (Windows) — most powerful.
- **speedscope.app** (any) — friendliest UI.
- **`flamegraph.pl` (Brendan Gregg's original)** — works on perf.data; less .NET-aware.
- **Honeycomb / Datadog "tracing as flames"** — for distributed tracing, often built-in.
