# Performance tuning

PLINQ has a small set of dials. Use them with measurement.

## `WithDegreeOfParallelism(N)`

Caps worker count. Defaults to `Environment.ProcessorCount`. Reasons to lower:

- The work is memory-bound (more cores ≠ more bandwidth).
- The work hits a shared lock or limited resource (DB connection pool of N → cap at N).
- Hyper-threaded box where ALU contention hurts more than overlap helps.

Reasons to raise: rare; PLINQ doesn't oversubscribe well because it's CPU-focused.

## `WithMergeOptions(...)`

See [MergeStrategies](../MergeStrategies/). Default `AutoBuffered` is usually fine. Switch to `NotBuffered` for first-result latency.

## `WithCancellation(token)`

Pass through. PLINQ checks the token periodically; long bodies should also check it.

## `WithExecutionMode(ParallelExecutionMode.ForceParallelism)`

By default PLINQ may run sequentially if it estimates parallelism wouldn't help (small source, expensive setup). Force it on with this option for benchmarking comparisons.

## When PLINQ wins big

| Scenario | Speedup |
|---|---|
| 1M items, ~10 µs body, no shared state | ~ProcessorCount × |
| Aggregations with the four-arg form | ~ProcessorCount × |
| `Where` with expensive predicate | linear with cores |

## When PLINQ loses

- **Tiny per-item work.** Partition + merge overhead dominates. `data.AsParallel().Sum()` of an int array is *slower* than `data.Sum()`.
- **Heavy contention inside the body.** A locked update cancels the parallelism.
- **Allocator-heavy bodies.** Per-thread GC pressure can serialise on the GC.
- **Out of cache.** Memory bandwidth saturates before cores do.

## Diagnosing

Profile with BenchmarkDotNet. Compare:

1. Sequential LINQ.
2. PLINQ with `WithMergeOptions(NotBuffered)`.
3. `Parallel.For` with `localInit`/`localFinally`.
4. Hand-rolled chunking with `Task.WhenAll` of partition tasks.

The fastest wins. There's no magic — PLINQ is one valid implementation; sometimes another is faster.

## A quick rule

If your body is:

- < 100 ns: probably sequential is faster.
- 100 ns–1 µs: maybe PLINQ helps; benchmark.
- > 1 µs and parallelisable: PLINQ probably wins.
- > 100 µs: parallelism is almost certainly worth it; pick the friendliest tool.
