# Synchronisation vs Asynchrony — overview

These two words are routinely confused with **blocking** vs **non-blocking** and **sequential** vs **parallel**. They are not synonymous.

## Three orthogonal axes

| Axis | "Sync" pole | "Async" pole |
|---|---|---|
| **When does control return?** | When the work is done | Immediately; completion is signalled later |
| **Does the calling thread block?** | Often (but not necessarily) | Often not (but not guaranteed) |
| **Is there parallelism?** | Maybe | Maybe |

`stream.Read()` is *synchronous* (control returns when done) and *blocking* (the thread is parked while the disk fetches data). `stream.ReadAsync()` is *asynchronous* (control returns immediately, you `await` later) and *typically non-blocking* (the thread is freed). But you can have:

- **Sync non-blocking**: a tight CPU loop. Returns when done; never blocks.
- **Async blocking**: an `async` method that internally does `Thread.Sleep`. Returns a `Task`, but the worker thread is parked. **Anti-pattern.**

## Read deeper

| File | What it covers |
|---|---|
| [01-Four-Quadrants.md](01-Four-Quadrants.md) | Sync/async × blocking/non-blocking — all four cells with real examples |
| [02-Async-Mechanics.md](02-Async-Mechanics.md) | What `async`/`await` actually compiles to: state machines, awaiters, continuations |
| [03-Common-Confusions.md](03-Common-Confusions.md) | `.Result`, `.Wait()`, `async void`, sync-over-async, async-over-sync, `ConfigureAwait(false)` |

## What `async/await` actually buys you

Two things:

1. **Concurrency without thread pinning.** A request that awaits IO releases its worker; another request can run on it. This is how an ASP.NET Core process handles thousands of concurrent requests on a thread pool of dozens.
2. **Composability.** `await` makes "wait for async result, then continue" look like sequential code. Without it, callbacks pyramid.

It does **not** automatically buy you:

- Parallelism. `async` is *concurrency*; parallelism comes from running on multiple threads, which `Task.Run` and `Parallel.*` provide.
- Non-blocking IO at the kernel level. That's the IO API's job — `ReadAsync` on a `FileStream` may still block if the underlying handle is opened synchronously, because Windows file IO is synchronous by default.
- Thread safety. An `async` method that mutates shared state has the same race conditions as a synchronous one would.

## See also

- [08-Async-Await-Deep-Dive](../../08-Async-Await-Deep-Dive/) — the state machine, the synchronisation context, `ConfigureAwait`, allocation-free patterns.
- [18-Pitfalls/SyncOverAsync](../../18-Pitfalls-and-Anti-Patterns/SyncOverAsync) — `.Result`/`.Wait()`-induced deadlocks.
