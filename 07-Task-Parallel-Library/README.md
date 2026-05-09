# 07 — The Task Parallel Library

> **Layer:** CLR + BCL
> **Reading time:** ~50 minutes
> **Prereq:** [03](../03-ThreadPool/), [04](../04-Synchronization-Primitives/)

The TPL is the unifying abstraction for "do work, possibly in parallel, possibly asynchronously, and let the runtime pick where." It's wrapped by `async/await`, by `Parallel.*`, and by countless library APIs.

This chapter is divided into:

| Folder | Topic |
|---|---|
| [TaskLifecycle](TaskLifecycle/) | States, exceptions, cancellation, continuations |
| [TaskSchedulers](TaskSchedulers/) | The default scheduler, custom schedulers, `LimitedConcurrencyLevelTaskScheduler` |
| [Parallel.For](Parallel.For/) | The original parallel loop primitive; partitioning, `localInit`/`localFinally` |
| [Parallel.ForEach](Parallel.ForEach/) | Same with `IEnumerable<T>`; chunking strategies |
| [Parallel.ForEachAsync](Parallel.ForEachAsync/) | The .NET 6+ async-friendly variant; **the right choice for IO fan-out** |
| [TaskCreationOptions](TaskCreationOptions/) | The flags that tweak Task behaviour |
| [ValueTask](ValueTask/) | Allocation-free async; rules of usage |
| [StructuredConcurrency](StructuredConcurrency/) | The pattern for cancellable, scoped, fail-fast task groups |

## Key types in one page

```csharp
Task                    // unit of work, no result
Task<TResult>           // unit of work, with result
ValueTask               // allocation-free Task wrapper for hot paths
ValueTask<TResult>      // ditto with result
TaskCompletionSource<T> // promise that you fulfill manually
TaskScheduler           // decides where tasks run
Parallel.For/ForEach[/Async] // declarative parallelism
PLINQ (Chapter 11)      // declarative data-parallelism over IEnumerable
```

## When to pick which

| Situation | Pick |
|---|---|
| Async one-off | `Task` / `Task<T>` via `async`/`await` |
| Async hot path with rare allocation | `ValueTask` / `ValueTask<T>` (read [ValueTask](ValueTask/) before using) |
| Manual completion (bridging events to async) | `TaskCompletionSource<T>` (always with `RunContinuationsAsynchronously`) |
| Parallel CPU loop over a collection | `Parallel.For` / `Parallel.ForEach` |
| Parallel IO-bound loop over a collection | `Parallel.ForEachAsync` |
| Custom scheduling (sequential, limited, affinity) | Custom `TaskScheduler` (rarely needed) |

## What `Task.Run` actually does

`Task.Run(action)` queues `action` to the `ThreadPool` via `TaskScheduler.Default`. It captures the calling `ExecutionContext` (so `AsyncLocal<T>` flows). It returns a `Task` whose continuations run after `action` completes.

`Task.Run` is **not** for already-async work. `Task.Run(() => SomeAsync())` queues the *invocation* of `SomeAsync`, but `SomeAsync` itself returns a `Task` immediately on its first `await`; the wrapping `Task.Run` finishes when the *invocation* finishes — which is *not* when the inner async completes. Use `Task.Run(async () => await SomeAsync())` if you actually need to offload.

## `Task.WhenAll` exception semantics

`await Task.WhenAll(tasks)` rethrows **only the first** exception in the AggregateException. To inspect all:

```csharp
try { await Task.WhenAll(tasks); }
catch
{
    var failures = tasks.Where(t => t.IsFaulted).Select(t => t.Exception!).ToList();
    // ... process failures
    throw;
}
```

This is the source of countless "where did all my errors go?" production debugging sessions.

## `Task.WhenEach` (.NET 9)

A new helper: returns an `IAsyncEnumerable<Task>` that yields tasks **in completion order**. Replaces hand-rolled "as-they-come" patterns:

```csharp
await foreach (var t in Task.WhenEach(tasks))
{
    var result = await t;            // never blocks; t is already complete
    ProcessAsResultArrives(result);
}
```

## Run

```bash
dotnet run --project 07-Task-Parallel-Library
```
