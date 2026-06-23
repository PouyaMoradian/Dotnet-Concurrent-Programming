# Definitions

The Pike quote is catchy but a more operational definition is more useful:

- **Concurrency** is a property of *program structure*. A program is concurrent if it has multiple independent control flows ("activities", "tasks") that are conceptually in flight at the same time. They may interleave on one CPU or run on many; either way, the program *is structured* to admit that interleaving.
- **Parallelism** is a property of *execution*. A program executes in parallel when those activities run literally simultaneously on multiple cores.

These are two independent dimensions:

| | Sequential | Concurrent |
|---|---|---|
| **Single-core** | Classic single-threaded program | Event loop / single-thread async (Node.js; .NET UI thread with `await`) |
| **Multi-core** | One thread, but SIMD/vectorised inside | Multiple threads / tasks (most .NET server code) |

Read the table corner by corner:

- **Top-left** — neither concurrent nor parallel. A short script that processes one input and exits.
- **Top-right** — concurrent but not parallel. A UI event loop juggling several `async` operations on one thread. Two `awaits` are "in flight" but at most one is *executing* at any moment.
- **Bottom-left** — parallel but not concurrent. A single-thread program that nonetheless gets parallelism *inside* an instruction via SIMD (`Vector256<T>`). The program structure is sequential; the hardware is parallel.
- **Bottom-right** — both. The mainstream case for modern servers: many concurrent requests, each potentially using multiple cores.

## Why this matters in .NET

| Problem | Right tool | Reason |
|---|---|---|
| Hide IO latency in a request handler | `async/await` | Concurrent, parallelism unnecessary |
| Crunch a 1 GB matrix | `Parallel.For` / PLINQ / SIMD | Parallelism is the point |
| Process queue items as they arrive | `Channel<T>` + workers | Concurrent **and** parallel |
| Run 10 sub-queries to fan-in | `Task.WhenAll` | Concurrent; parallel only if they hit different threads |
| Process a list of 1M tiny strings | `for` loop | Neither — no overhead is justified |

The mistake we see most often: people parallelise IO-bound work (`Parallel.ForEach` over `HttpClient.GetAsync`). It works but it pins worker threads spinning, instead of using `Parallel.ForEachAsync` (which gives you concurrency *and* a concurrency cap, without thread-pinning). See [07-TPL/Parallel.ForEachAsync](../../07-Task-Parallel-Library/Parallel.ForEachAsync).

## A subtle but important point

`async/await` on a server with no `SynchronizationContext` (ASP.NET Core, console apps) **is** parallel by default — every `await` may resume on a different pool thread. So in modern ASP.NET, async is concurrent *and* incidentally parallel. The single-threaded "event loop" is a UI / WinForms artefact.

That distinction matters when you read advice elsewhere on the internet. Node.js advice about "the event loop" doesn't translate to ASP.NET Core; .NET advice from 2010 about WinForms doesn't translate to modern web apps.

## A test of understanding

Which of the following programs are concurrent? Which are parallel?

1. A console app that calls `Math.Sqrt` in a `for` loop a million times.
2. A WPF app that calls `await Task.Delay(1000)` on a button click, with no `Task.Run`.
3. An ASP.NET Core handler that calls `await db.Query()` followed by `await http.Get()` sequentially.
4. A console app that calls `Parallel.For(0, n, i => Math.Sqrt(i))`.
5. A console app that does `await Task.WhenAll(Task.Run(work1), Task.Run(work2))`.

Answers:

1. Neither.
2. Concurrent (the UI is interactive during the await), not parallel (one thread).
3. Concurrent; possibly parallel within the runtime's IO threads but the *handler logic itself* is sequential between awaits.
4. Parallel (and concurrent, but the parallelism is the point).
5. Concurrent and parallel — two pool threads, each running CPU work.

If those answers surprised you, re-read the table. The mismatch usually traces to conflating "concurrent" with "uses threads".
