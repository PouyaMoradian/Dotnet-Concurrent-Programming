# 08 — Async/Await Deep Dive

> **Layer:** C# compiler + CLR + BCL
> **Reading time:** ~60 minutes
> **Prereq:** [03](../03-ThreadPool/), [07](../07-Task-Parallel-Library/)

`async/await` is the most-used and least-understood C# feature. It is *not* about threads. It is *not* about parallelism. It is a syntactic transformation that lets you write callback-based code as if it were sequential, with all the type-safety, exception propagation, and debugging that ordinary methods get.

This chapter is the longest README in the repo. Read it in order.

## What you must understand before writing async code

| Section | Topic |
|---|---|
| [StateMachines](StateMachines/) | What the compiler emits; how `await` actually suspends and resumes |
| [SynchronizationContext](SynchronizationContext/) | Why "where does this resume?" depends on the host |
| [ExecutionContext](ExecutionContext/) | What flows across awaits (AsyncLocal), what doesn't (ThreadStatic) |
| [ConfigureAwait](ConfigureAwait/) | When `(false)` matters, when it doesn't, the new options |
| [AsyncStreams](AsyncStreams/) | `IAsyncEnumerable<T>` and `await foreach` |
| [IAsyncDisposable](IAsyncDisposable/) | When ordinary `IDisposable` isn't enough |
| [AsyncOverSync](AsyncOverSync/) | Wrapping sync code in `Task.Run` — when it's right, when it's a lie |
| [AllocationFreeAsync](AllocationFreeAsync/) | `ValueTask`, pooled state machines, `IValueTaskSource` |

## The 5-line tl;dr

1. `async` methods return `Task` / `Task<T>` / `ValueTask` (etc.). They never block their caller.
2. `await` suspends the method until the awaitable completes; the rest of the method becomes a continuation.
3. The continuation runs on the captured `SynchronizationContext` (or, with `ConfigureAwait(false)`, on whoever finishes the awaitable).
4. **`AsyncLocal<T>` flows across awaits. `ThreadStatic` does not.**
5. Don't `async void`. Don't `.Result` / `.Wait()`. Don't lock across awaits. Pass `CancellationToken` everywhere.

## What the compiler emits — quick preview

```csharp
public async Task<int> FooAsync()
{
    var x = await ComputeAsync();
    return x + 1;
}
```

The compiler generates a struct (or class in DEBUG) state machine that:

- Stores locals as fields.
- Has a `MoveNext()` method with a switch over a `_state` field.
- Suspends by hooking up a continuation on the awaiter.
- Resumes by re-entering `MoveNext()` from a known state.

See [StateMachines](StateMachines/) for the disassembly walk-through.

## Where async resumes — the sync context dance

| Host | Default capture | Effect |
|---|---|---|
| ASP.NET Core | none | resumes on whichever pool thread finished the await |
| Console app | none | same |
| WinForms / WPF | the UI sync context | resumes back on the UI thread |
| Old ASP.NET (Framework) | request context | resumes on the request thread; subject to deadlock with sync-over-async |
| Custom (test frameworks, vendor SDKs) | varies | depends on the SDK |

`ConfigureAwait(false)` says "I don't care; resume anywhere." In **library code** that doesn't touch UI / per-request state, *always* `ConfigureAwait(false)`. In **app code** running on ASP.NET Core / consoles, it makes no difference (no ambient context to capture).

## "Async is viral" — and that's a feature

Once a method is `async`, callers must either `await` it or stay sync (and risk sync-over-async). This is by design: the type system *forces* you to think about completion. Embrace it; don't fight it with `.Result`.

## Run

```bash
dotnet run --project 08-Async-Await-Deep-Dive
```
