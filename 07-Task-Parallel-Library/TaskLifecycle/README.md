# Task lifecycle

Every `Task` walks through these states:

```
Created  →  WaitingForActivation  →  WaitingToRun  →  Running  →  RanToCompletion
                                                              ↘  Faulted
                                                              ↘  Canceled
```

For an async method's returned task, "Created" is skipped — it's already activated. For `Task.Run`, the task enters `WaitingToRun` (queued).

## Statuses you'll observe

| `TaskStatus` | Meaning |
|---|---|
| `WaitingForActivation` | Async method's task; waiting on an awaiter |
| `WaitingToRun` | Queued on a scheduler; not yet started |
| `Running` | A worker is executing the body |
| `WaitingForChildrenToComplete` | Body finished but attached children still running |
| `RanToCompletion` | Done, no exception |
| `Faulted` | Done with exception(s); access via `.Exception` |
| `Canceled` | Body or awaiter observed a `OperationCanceledException` matching the linked token |

## Exceptions

A `Task`'s `.Exception` is an `AggregateException`. When you `await` the task, the *first* inner exception is rethrown (with stack trace preserved). When you `.Wait()` or `.Result`, the `AggregateException` is rethrown as-is.

> Modern code: always `await`. The `AggregateException` machinery is for `Wait`/`Result`/`ContinueWith` patterns that predate async.

## Continuations

`ContinueWith` is the pre-async-era successor mechanism:

```csharp
task.ContinueWith(t => /* ... */, TaskContinuationOptions.OnlyOnFaulted)
    .ContinueWith(t => /* ... */, TaskContinuationOptions.OnlyOnRanToCompletion);
```

`async/await` superseded it for almost all cases. The remaining valid uses:

- Telemetry / logging that should run regardless of outcome (`OnlyOn...` flags).
- Bridging non-async libraries to async (still better via `TaskCompletionSource<T>`).

**Don't mix `ContinueWith` and `await` in new code unless you have a reason.**

## Cancellation

A task is `Canceled` (not `Faulted`) when:

1. It throws `OperationCanceledException`, AND
2. The exception's `CancellationToken` matches the token observed by the task's machinery.

`throw new OperationCanceledException()` from a method called by `Task.Run` results in `Faulted`, *not* `Canceled` — because no token was associated. To cancel:

```csharp
ct.ThrowIfCancellationRequested();        // throws OperationCanceledException(ct)
```

Or pass the token to a cancelable async API (`Task.Delay(timeout, ct)`).

## `Task.Run` vs `Task.Factory.StartNew`

`Task.Run` is the right answer 99% of the time. It uses:

- `TaskScheduler.Default` (the pool).
- `TaskCreationOptions.DenyChildAttach` (children are detached, not attached — a stricter, safer default).

`Task.Factory.StartNew` exposes more knobs but is easy to misuse. Common mistakes:

- Forgetting `TaskScheduler.Default` → uses ambient `TaskScheduler.Current`, which may be a UI scheduler.
- Forgetting `DenyChildAttach` → child tasks block the parent's completion in surprising ways.
- Wrapping an already-async method → returns `Task<Task>`; callers `await` once and miss the inner task. *Use `Task.Run(async () => …)` and you'll get `Task` directly.*

## Long-lived tasks

For tasks that intentionally never complete (a worker loop), make sure exceptions are observed (`task.ContinueWith(t => Log(t.Exception), OnlyOnFaulted)` or wrap in a try/catch inside the body). Unobserved task exceptions get collected by the GC and routed to `TaskScheduler.UnobservedTaskException` — a great way to lose errors silently if you don't subscribe.
