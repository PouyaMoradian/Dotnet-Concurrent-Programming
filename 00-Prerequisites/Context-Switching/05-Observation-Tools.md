# Observing context switches

You cannot tune what you cannot see. This file is the toolbox for actually measuring the scheduler's behaviour and the thread pool's state in a running .NET process. Pick by the level of detail you need.

## Level 1 — `dotnet-counters` (live numbers, no setup)

`dotnet-counters` ships with the SDK. Attach to a running process:

```bash
dotnet-counters monitor -p <pid> System.Runtime
```

Key counters that surface scheduling and locking:

| Counter | What it measures |
|---|---|
| `threadpool-thread-count` | Live pool worker count. If this is rising during steady-state, the pool is reactively growing — usually because of blocking. |
| `threadpool-queue-length` | Items waiting for a worker. >0 sustained means workers are saturated. |
| `threadpool-completed-items-count` | Total work items completed (cumulative). Useful for throughput tracking. |
| `monitor-lock-contention-count` | Times `lock`/`Monitor.Enter` had to wait. High = contention; investigate. |
| `lock-contention-count` (per `Lock` type in .NET 9+) | Same, for the new `System.Threading.Lock`. |
| `gc-heap-size` | Helps explain pauses (GC pauses generate context switches too). |
| `time-in-gc` | % time in GC. Spikes correlate with switches around suspension. |

This is the first thing to look at when "the server is slow but I don't know why".

## Level 2 — `dotnet-trace` (event-level capture)

`dotnet-trace` collects EventPipe events:

```bash
dotnet-trace collect -p <pid> --providers Microsoft-DotNETCore-SampleProfiler,Microsoft-Windows-DotNETRuntime:0x10000:0
```

Useful providers for context-switch / contention analysis:

| Provider / keyword | Why |
|---|---|
| `Microsoft-Windows-DotNETRuntime:0x10000` (Contention) | Per-lock contention start/stop events with thread IDs |
| `Microsoft-Windows-DotNETRuntime:0x100` (Threading) | Pool worker creation, IO completion thread creation |
| `Microsoft-Windows-DotNETRuntime:0x8000000` (Threadpool work events) | Each item enqueued/dequeued/started |
| `Microsoft-DotNETCore-SampleProfiler` | Sampled call-stacks (CPU profiling) |

Open the resulting `.nettrace` in PerfView, Speedscope, or Visual Studio's Performance Profiler. PerfView's "Thread Time" view is invaluable for spotting blocked-time gaps.

## Level 3 — PerfView / VS Concurrency Visualizer (Windows)

PerfView is the deepest-detail tool for .NET on Windows. After `Run Command...` or `Collect`:

- **Stack window**: who's calling what.
- **Thread Time**: per-thread CPU vs blocked-on-sync, with stacks for both states.
- **Lock holding times**: per-lock, who held it, for how long.
- **Context switch events**: when the **ETW** (Event Tracing for Windows) kernel logger is also enabled, you get per-CPU timelines of every switch on the system.

Visual Studio's *Concurrency Visualizer* (a free extension) gives a similar per-thread timeline view in a more clicking-around-friendly form. Less depth, more accessible.

## Level 4 — Linux `perf` and BPF tools

(**BPF** = Berkeley Packet Filter, originally a packet filter, now a general-purpose in-kernel virtual machine for safe tracing; **eBPF** is the modern extended form.)

On Linux you have direct access to scheduler events:

```bash
# Count switches and migrations.
perf stat -e context-switches,cpu-migrations,cache-misses ./yourapp

# Per-thread runqueue latency (how long threads waited to run after becoming runnable).
sudo runqlat                # from bcc-tools or bpftrace

# Off-CPU profiling — where threads block.
sudo offcputime -p <pid> 30 > offcpu.stacks
# Visualise with FlameGraph: ./flamegraph.pl --color=io offcpu.stacks
```

`runqlat`'s histogram is the smoking gun for over-subscription: if many runnable threads are waiting >1 ms to get on CPU, you have too many threads competing for too few cores.

`offcputime` complements regular CPU profilers: regular profilers tell you where threads *ran*; off-CPU tells you where they *blocked*. For a server that's "slow but not CPU-bound", off-CPU is where the answer lives.

## Level 5 — code-level instrumentation

When the scheduler-level tools point at *your code*, drop measurement in:

```csharp
using System.Diagnostics;

private static long _waited;
private static long _entered;

public static void DoWork()
{
    var swWait = Stopwatch.StartNew();
    lock (_lock)
    {
        swWait.Stop();
        Interlocked.Add(ref _waited, swWait.ElapsedTicks);
        Interlocked.Increment(ref _entered);
        // ... real work ...
    }
}

public static (long avgWaitNs, long count) GetStats()
{
    long entered = Interlocked.Read(ref _entered);
    long waited = Interlocked.Read(ref _waited);
    return (waited * 1_000_000_000 / Stopwatch.Frequency / Math.Max(1, entered), entered);
}
```

(A real implementation would use `System.Diagnostics.Metrics` and a histogram counter.)

## What "normal" looks like

Rough baselines on a healthy 16-thread server doing ~10k RPS of mostly-async work:

| Counter | Expected |
|---|---|
| `threadpool-thread-count` | 16–32 |
| `threadpool-queue-length` | 0 most of the time |
| `monitor-lock-contention-count` | 0–100 per second |
| Voluntary switches (`/proc/<pid>/status`) | thousands per second |
| Involuntary switches | dozens per second |
| `runqlat` p99 | <500 µs |

Outliers point at a specific problem:

- High involuntary switches + low pool growth → CPU oversubscription.
- High pool growth + low CPU → thread-pool starvation (sync-over-async).
- High contention count → a lock to break up.
- High GC time → GC is causing many switches; address allocations.

## Practical takeaways

- `dotnet-counters` first. It's free, live, and tells you 80% of what you need.
- `dotnet-trace` when you need provenance — which call site, which lock.
- `perf` / BPF for system-level questions (runqueue latency, off-CPU stacks).
- Code-level metrics when you've narrowed to a specific section. `System.Diagnostics.Metrics` is the modern way.

## Further reading

- **Brendan Gregg — *BPF Performance Tools*** — the catalog of every BPF tool you'd want.
- **Sasha Goldshtein — *.NET Performance Tips and Tricks*** (talks, slides on PerfView).
- **`dotnet-counters` docs on learn.microsoft.com** — full counter reference.
- **`dotnet-trace` docs** — provider/keyword tables for all built-in EventSource providers.
