# BenchmarkDotNet

The standard for .NET microbenchmarking. Statistical rigour by default — runs warm-up, converges to a stable distribution, reports mean/median/stddev with confidence intervals.

## Project setup

```xml
<PackageReference Include="BenchmarkDotNet" />
```

Console app entry point:

```csharp
using BenchmarkDotNet.Running;

var summary = BenchmarkRunner.Run<MyBench>();
```

Run with `dotnet run -c Release` — **never debug**; .NET in Debug skips inlining and tier-0 isn't representative.

## A solid baseline

```csharp
[MemoryDiagnoser]                                 // shows allocations
[SimpleJob(RuntimeMoniker.Net100, baseline: true)]
[SimpleJob(RuntimeMoniker.Net90)]                 // compare versions
public class MyBench
{
    private readonly int[] _data = Enumerable.Range(0, 10_000).ToArray();

    [Benchmark(Baseline = true)] public long ForLoop()
    {
        long s = 0;
        for (var i = 0; i < _data.Length; i++) s += _data[i];
        return s;
    }

    [Benchmark] public long Linq() => _data.Sum();
}
```

Output:

```
| Method  | Mean    | Error  | StdDev | Ratio | Allocated |
|---------|---------|--------|--------|-------|-----------|
| ForLoop | 5.42 µs | 0.05µs | 0.04µs | 1.00  | -         |
| Linq    | 4.10 µs | 0.03µs | 0.02µs | 0.76  | -         |
```

`Ratio` < 1 means faster than baseline. `Allocated` `-` means zero managed allocations.

## Concurrency-relevant tips

- **`[ThreadingDiagnoser]`** captures lock contention and thread events.
- **`[HardwareCounters(HardwareCounter.CacheMisses, HardwareCounter.BranchMispredictions)]`** for cache/branch effects (Windows only via PMU).
- **`[DisassemblyDiagnoser]`** shows the JITted assembly — essential for "why is this faster?"

## Multi-thread benchmarks

```csharp
[Benchmark]
public async Task ParallelBench()
{
    await Parallel.ForEachAsync(Enumerable.Range(0, 1000),
        new ParallelOptions { MaxDegreeOfParallelism = 8 },
        async (_, _) => { Workloads.Cpu(10_000); });
}
```

BenchmarkDotNet runs the body many times and averages. It's not a load test — for sustained behaviour use `crank` or NBomber.

## Common BDN gotchas

1. **Method must return / write to volatile**. Otherwise the JIT may elide it. Either return a value or `[MemoryDiagnoser]` something the JIT can't fold.
2. **Setup vs Iteration**. Use `[GlobalSetup]` for one-time work, `[IterationSetup]` for per-iteration (rare; expensive).
3. **`[Params]`** for varying inputs:
   ```csharp
   [Params(10, 100, 1000)] public int N;
   ```
4. **Don't benchmark in Debug**.
5. **Don't benchmark on a contended box**. Close Slack, browsers, the world.
6. **A 5% difference is usually noise.** Demand 20%+ for "fast enough to claim faster".

## Reading the output

`Ratio` and `RatioSD` are key. `RatioSD` (the std dev of the ratio) tells you how confident the comparison is. If `RatioSD` is large vs `Ratio - 1`, the difference may be noise.

## What BDN can't do

- **Macro-benchmarks** (full-app load tests). Use `crank` for that.
- **Real-world cache effects** at scale (multiple workers competing). For multi-thread perf, hand-roll something (with random seeds and thread pinning) or use `ThreadingDiagnoser` + a parallel benchmark.
- **GC under sustained load** — single-shot benchmarks don't reach steady GC state.
