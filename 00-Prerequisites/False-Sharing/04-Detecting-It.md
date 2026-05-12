# Detecting false sharing

False sharing is invisible in source. It is invisible to correctness tests. It is invisible to non-concurrent profilers. It shows up only as a *throughput cliff* under multi-threaded load, and it cures only when the right cache line stops moving. This file is the toolbox for proving it's the bug.

## Symptom 1 — sub-linear scaling

```
Workers   Throughput (events/sec)
 1        10 M
 2        8  M
 4        4  M
 8        2  M
```

A workload that goes *backward* with more cores is almost always coherence-bound. The candidates: a single shared atomic on the hot path, lock contention, or false sharing. A profiler that shows ~no time in `lock` and no contention metrics points to false sharing.

## Symptom 2 — high cache misses on threads that shouldn't be missing

A loop that increments a `long` should hit L1 every time. If a profiler shows L1 miss rate >10% on that loop, the line is being kicked out — coherence traffic.

BenchmarkDotNet exposes this through hardware counters:

```csharp
[Config(typeof(Config))]
public class FalseSharingBench
{
    [HardwareCounters(HardwareCounter.CacheMisses, HardwareCounter.BranchMispredictions)]
    private class Config : ManualConfig
    {
        public Config() => AddDiagnoser(new BenchmarkDotNet.Diagnostics.Windows.EtwProfiler.EtwProfiler());
    }

    // ... [Benchmark] methods ...
}
```

The "Hardware counters" column will show CacheMisses per operation. A padded baseline at ~0.001 vs a packed variant at ~1.5 is the unambiguous signal.

On Linux, `perf stat`:
```bash
perf stat -e cache-misses,cache-references,L1-dcache-load-misses ./yourapp
```

L1 miss rate >5% on a loop that should be entirely L1-resident says "lines are being evicted by coherence traffic".

## Symptom 3 — `perf c2c` shows cross-cache transfers

This is the most direct detector. `perf c2c` (Linux ≥4.10) explicitly reports cache-line transfers between cores:

```bash
sudo perf c2c record -F 99 ./yourapp
sudo perf c2c report --node-info,times
```

The report has a "Shared Data Cache Line Table". Each row is one cache line, with columns for:

- Total **HITM** events (a `perf c2c` event class for "the cache line was found in another core's L1 in Modified state"; the H/I/T/M letters are not an acronym but a packet-type tag).
- The virtual address of the line.
- Which cores hit it.
- The source-level information (PID, function, instruction) if your binary has debug info.

For .NET, the source-level info will be ambiguous (JIT-compiled code, dynamic addresses). But the *fact* that line X is hot and ping-ponging is unambiguous.

Combine with the `pid_filter` and run twice — padded and unpadded — to see the line disappear.

## Symptom 4 — `dotnet-trace` and ETW

Less precise than `perf c2c` but works on Windows:

```powershell
dotnet-trace collect --providers Microsoft-Windows-DotNETRuntime:0x1:5 --duration 00:00:10
```

The **ETW** (Event Tracing for Windows) kernel logger with `+CSWITCH+DPC+INTERRUPT+SAMPLEDPROFILE` (CSWITCH = context switch events; **DPC** = Deferred Procedure Call — a Windows kernel mechanism for deferring interrupt-time work to a lower IRQL) shows **IPI** (Inter-Processor Interrupt) activity, which correlates with coherence traffic — but not as directly as `perf c2c`.

PerfView's "Memory" view can sometimes call out hot lines if you collected enough events.

## Symptom 5 — by inspection of the layout

For suspected sites:

```csharp
unsafe
{
    fixed (long* p = &SomeStruct._field)
    {
        long addr = (long)p;
        Console.WriteLine($"Field at 0x{addr:X}; line offset: {addr % 64} (or % 128 on Apple)");
    }
}
```

Two suspected-false-shared fields whose addresses differ by less than 64 (or 128) share a line. This is a quick sanity check before reaching for `perf c2c`.

For arrays:
```csharp
fixed (long* p = &arr[0]) Console.WriteLine($"&arr[0] = 0x{(long)p:X}");
fixed (long* p = &arr[1]) Console.WriteLine($"&arr[1] = 0x{(long)p:X}");
```

Adjacent longs differ by 8. Adjacent `PaddedLong` instances differ by 128.

## Symptom 6 — A/B test the fix

The cheapest detector is: *change the layout and time it*. If padding kills the slowdown, false sharing was the cause. If it doesn't, look elsewhere (real contention, allocation pressure, GC pauses, etc.).

```csharp
[Benchmark] public void Packed()  { /* uses Packed struct */ }
[Benchmark] public void Padded()  { /* uses PaddedLong struct */ }
```

A clean 5–10× ratio under multi-threading is the smoking gun. Within 5% — false sharing isn't the issue.

## False positives and noise

- **Cold-start variance.** First touch of a memory region is slower than steady-state. Always warm up the benchmark.
- **Hardware prefetching.** A loop that walks `arr[i]` may prefetch `arr[i+8]` ahead, masking some false sharing. Random access patterns expose it more clearly.
- **Background CPU activity.** Run on an isolated CPU (`taskset` / `numactl`) for benchmarking.
- **GC pauses.** A GC pause looks like throughput dropping; differentiate using `dotnet-counters` (`time-in-gc`).

## A diagnostic checklist

When you suspect false sharing:

1. **Inspect the source.** Are two hot fields adjacent in a struct/class, written from different threads? Or an array of primitives indexed by thread?
2. **Compute layout offsets.** Are they within the cache-line distance?
3. **A/B test with padding.** Quickest signal.
4. **Use `perf c2c`** if you need to be 100% sure or to find the line in a large codebase.
5. **Apply the fix.** Confirm with a measurement.
6. **Document.** Add a comment near the padded field explaining *why*. Future you will thank you.

## Practical takeaways

- "Sub-linear scaling + ~no contention" → suspect false sharing.
- BenchmarkDotNet's hardware counters and Linux's `perf c2c` are your two best friends.
- A/B testing with padding is the cheapest possible diagnostic — try it before drilling deeper.
- Detection is straightforward once you know to look; the bug is usually in the layout of one specific data structure.

## Lab

`FalseSharingDemo` (demo 1) shows the throughput difference directly. To exercise *detection*:

1. Run the demo under `perf stat -e cache-misses,instructions` (Linux).
2. The packed variant should show ~1+ cache-misses per increment.
3. The padded variant should show <0.01 per increment.

That's the difference between "every increment goes through the interconnect" and "every increment stays in L1".

## Further reading

- **Joe Mario — *c2c: false sharing detection in Linux perf*** (blog post on LWN).
- **Sasha Goldshtein — *Pragmatic Lock-Free Programming***  (talk; covers detection in PerfView).
- **`perf-c2c(1)` man page** — the user-space contract.
- **BenchmarkDotNet `[HardwareCounters]` docs**.
