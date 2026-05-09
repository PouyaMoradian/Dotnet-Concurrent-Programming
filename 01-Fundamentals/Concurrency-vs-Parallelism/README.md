# Concurrency vs Parallelism

> "Concurrency is about *dealing* with many things at once. Parallelism is about *doing* many things at once." — Rob Pike

## Definitions

- **Concurrency** is a property of *program structure*: there are independent tasks in flight.
- **Parallelism** is a property of *execution*: those tasks run literally simultaneously on multiple cores.

These are independent dimensions:

|  | Sequential | Concurrent |
|---|---|---|
| **Single-core** | classic single-threaded program | event loop, single-thread async |
| **Multi-core** | one thread, but parallel SIMD inside | multiple threads / tasks |

## Why the distinction matters in .NET

| Problem | Right tool | Reason |
|---|---|---|
| Hide IO latency in a request handler | async/await | concurrent, parallelism unnecessary |
| Crunch a 1 GB matrix | `Parallel.For` / PLINQ / SIMD | parallelism is the point |
| Process queue items as they arrive | `Channel<T>` + workers | concurrent **and** parallel |
| Run 10 sub-queries to fan-in | `Task.WhenAll` | concurrent; parallel only if they hit different threads |

The mistake we see most often: people parallelise IO-bound work (`Parallel.ForEach` over `HttpClient.GetAsync`). It works but it pins worker threads spinning, instead of using `Parallel.ForEachAsync` (which gives you concurrency *and* a concurrency cap, without thread-pinning). See [07-TPL/Parallel.ForEachAsync](../../07-Task-Parallel-Library/Parallel.ForEachAsync).

## Demonstration

The `ConcurrencyVsParallelismDemo` runs:

1. 3 × 300 ms IO concurrently → ~300 ms (concurrent, almost no parallelism)
2. 3 × CPU loops in parallel → ~time_of_one_loop (parallelism does the work)
3. 3 × CPU loops sequentially → 3 × time_of_one_loop (neither)

Time them and the distinction becomes physical.

## A subtle but important point

`async/await` on a server with no `SynchronizationContext` (ASP.NET Core, console apps) **is** parallel by default — every `await` may resume on a different pool thread. So in modern ASP.NET, async is concurrent *and* incidentally parallel. The single-threaded "event loop" is a UI / WinForms artefact.
