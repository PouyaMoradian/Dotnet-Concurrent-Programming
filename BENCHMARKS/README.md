# BENCHMARKS

Every performance claim in this repository should be backed by a benchmark in this folder. Each benchmark project is a `dotnet run -c Release` away.

## Projects

| Project | What it measures |
|---|---|
| `ThreadPoolBenchmarks` | `Task.Run` cost, `ThreadPool.QueueUserWorkItem`, `UnsafeQueueUserWorkItem`, ThreadPool injection |
| `ChannelsBenchmarks` | Bounded vs unbounded; SingleReader/SingleWriter; vs `ConcurrentQueue<T>` + signalling |
| `LockContentionBenchmarks` | `lock` / `Lock` (.NET 9) / `Interlocked` / `SpinLock` under contention |
| `AsyncBenchmarks` | `Task<T>` vs `ValueTask<T>`; pooled vs not; sync-completing vs suspending |
| `AllocationBenchmarks` | LINQ vs for; closures vs static lambdas; struct vs class enumerators |

## Running

```bash
# all benchmarks in a project
dotnet run -c Release --project BENCHMARKS/ChannelsBenchmarks

# filter by name
dotnet run -c Release --project BENCHMARKS/ChannelsBenchmarks -- --filter '*BoundedSpsc*'
```

## Reading the output

The columns to focus on:

- **`Mean`** — average runtime per operation. Smaller is faster.
- **`Ratio`** — relative to the baseline (`[Benchmark(Baseline = true)]`). `< 1` means faster than baseline; `> 1` means slower.
- **`Allocated`** — bytes allocated per operation. Lower is better.
- **`StdDev`** — variability. If `StdDev` is large vs `Mean`, the result is noisy.

## House rules

1. **Run in `-c Release`.** Debug numbers are meaningless.
2. **Run on a quiet machine.** Background processes skew everything.
3. **Numbers vary by hardware.** Re-run on your machine; trust your results, not someone else's.
4. **Document the host.** A `dotnet --info` capture goes in `docs/benchmark-results/` per result you commit.

## Observed differences depend on hardware

A 5% difference is usually noise. A 20% difference is suspicious unless reproducible. A 2× difference is real. **Don't make claims without 20%+ and a fresh re-run.**

## Benchmarks present in this repo are starting points

The included projects have minimal sets of benchmarks. Extend them as you investigate specific claims. Pull requests welcome — see [CONTRIBUTING.md](../CONTRIBUTING.md).
