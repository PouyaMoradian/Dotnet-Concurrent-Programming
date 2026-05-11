# Concurrency vs Parallelism — overview

> "Concurrency is about *dealing* with many things at once. Parallelism is about *doing* many things at once." — Rob Pike

These two words are routinely treated as synonyms. They aren't. Confusing them is the proximate cause of two of the most common production bugs in .NET:

- **`Parallel.ForEach` over `HttpClient.GetAsync`** — wraps IO in a parallel construct, pins worker threads, scales worse than the async-friendly `Parallel.ForEachAsync`.
- **`async/await` around CPU-bound code in a tight loop** — wraps compute in an IO construct, gains nothing, sometimes loses to allocation overhead.

You only avoid both by being precise about which property — structure or execution — you actually need.

## TL;DR

| | Concurrency | Parallelism |
|---|---|---|
| What it is | A property of *program structure* — multiple independent activities in flight | A property of *execution* — those activities run literally at the same time |
| Hardware needed | None (works on a single core) | Multiple cores |
| What it buys you | Latency hiding, responsiveness, composition of independent operations | Throughput on CPU-bound work |
| Failure mode | Logical races, deadlocks | False sharing, oversubscription |
| Canonical .NET API | `async/await`, `Channel<T>`, `Task.WhenAll` | `Parallel.*`, PLINQ, SIMD, `Task.Run` for compute |

## Read deeper

| File | What it covers |
|---|---|
| [01-Definitions.md](01-Definitions.md) | Formal definitions, the quadrant table, why a single-threaded event loop is "concurrent but not parallel" |
| [02-Patterns.md](02-Patterns.md) | Concurrency patterns (request handler, producer/consumer, pipeline) vs parallelism patterns (data-parallel, fork-join, map-reduce) |
| [03-DotNet-Tooling.md](03-DotNet-Tooling.md) | Which .NET API maps to which property — and the four common mismatches |

## The canonical example

```
Concurrency: one cook, three pots, alternates stirring.
Parallelism: three cooks, three pots, simultaneous.
```

Both feed three pots faster than one-at-a-time. But the single cook only helps when *some of the stirring time overlaps with waiting* — the water heating, the pasta cooking. That overlap is what async hides. Without it (three pots that all need constant stirring), one cook can't win and only parallelism — three cooks — helps.

## Demonstration

`ConcurrencyVsParallelismDemo` runs three workloads back-to-back:

1. 3 × 300 ms async IO concurrently → ~300 ms total (concurrent, almost no parallelism).
2. 3 × CPU loops in parallel → ~time_of_one_loop (parallelism does the work).
3. 3 × CPU loops sequentially → 3 × time_of_one_loop (neither).

Time them on your machine and the distinction becomes physical.
