# `TaskCreationOptions`

The flags passed to `Task.Factory.StartNew` (and indirectly used by `Task.Run`) that change task behaviour. Most of these are subtle and rarely the right answer.

| Option | Meaning | When to use |
|---|---|---|
| `None` | Default | always for `Task.Run` |
| `LongRunning` | Hint: don't run on the pool; use a dedicated thread | seconds-to-minutes CPU work; see [03/LongRunning](../../03-ThreadPool/LongRunning) |
| `AttachedToParent` | This task becomes a child; parent doesn't complete until child does | almost never useful in modern code |
| `DenyChildAttach` | Child tasks created inside this body are *detached* | `Task.Run` sets this implicitly; explicit only if using `StartNew` |
| `PreferFairness` | Prefer global queue over local LIFO (FIFO-ish) | very specific testing; rarely production |
| `HideScheduler` | Inner tasks shouldn't see the same scheduler | nesting custom schedulers |
| `RunContinuationsAsynchronously` | (TaskCreationOptions on TCS) Continuations run on pool, not inline | **always for `TaskCompletionSource<T>`** |

## `RunContinuationsAsynchronously` for TCS

This is the single most important one. By default, `tcs.SetResult(...)` runs continuations *inline* on the calling thread. If a thousand things are awaiting that TCS, all their continuations run on whoever called `SetResult` — leading to:

- Stack overflow.
- Locks held by the caller being held while continuations run.
- Deadlocks if a continuation tries to acquire something the caller holds.

```csharp
// ✅ Right
var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

// ❌ Wrong (the default)
var tcs = new TaskCompletionSource<int>();
```

Yes, the default is wrong. .NET shipped this default in 2010 and we're stuck with it.

## `AttachedToParent` — the hidden ordering bug

```csharp
Task.Run(() =>
{
    Task.Factory.StartNew(Inner, TaskCreationOptions.AttachedToParent);
    // outer body returns…
});
// …but the outer task waits for Inner before completing!
```

This was the original "structured concurrency" mechanism in TPL but it conflicts with async semantics. `Task.Run` defaults to `DenyChildAttach` to avoid this footgun. **Don't use AttachedToParent in new code.**

## `LongRunning` revisited

`Task.Factory.StartNew(action, ..., LongRunning, TaskScheduler.Default)` allocates a dedicated thread (no pool worker). For "I want this to run for a long time without affecting the pool's hill-climbing decisions." Modern alternatives:

- For an indefinite worker: `BackgroundService` / `IHostedService` in DI.
- For a heavy compute: `LongRunning` is fine; consider `new Thread` if you also want to set affinity/priority.
- For an async loop: just call the async method; no `LongRunning` needed.

## Combining flags

Bitwise OR. `LongRunning | DenyChildAttach` is a common combo for spawn-and-forget worker tasks.
