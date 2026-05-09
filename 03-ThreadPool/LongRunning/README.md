# LongRunning tasks

`TaskCreationOptions.LongRunning` is a hint: "this task will run for a long time; don't put it on a pool worker."

## What it actually does

In current implementations the TPL allocates a *dedicated* `Thread` for the task instead of queuing on the pool. The thread is created with `IsBackground = true` and runs only that task's delegate, then terminates.

```csharp
Task.Factory.StartNew(
    () => RunForever(token),
    CancellationToken.None,
    TaskCreationOptions.LongRunning,
    TaskScheduler.Default);
```

## When to use it

- Tasks that run for **seconds to minutes** of mostly-CPU work.
- A long pump that periodically checks a `BlockingCollection` / `Channel`.
- Any work that would otherwise hold a pool worker for a duration that would distort hill-climbing.

## When **not** to use it

- For *async* loops (`while (!ct.IsCancellationRequested) await something()`). They don't pin a worker, so `LongRunning` would just allocate a thread that mostly sleeps. Use a normal `Task.Run` (or even just call the async method directly).
- For tasks that finish in milliseconds. The thread-creation cost dominates.
- For "I want an isolated thread." Use `new Thread { IsBackground = true }` directly — it's clearer to readers.

## Comparison

| Approach | When to choose |
|---|---|
| `Task.Run(action)` | Short bursts of CPU work; default choice |
| `Task.Factory.StartNew(action, …, LongRunning, …)` | Multi-second-to-minute CPU loops |
| `new Thread(action) { IsBackground = true }` + `Start()` | Indefinite worker; you want full control of priority/affinity |
| `BackgroundService` / `IHostedService` | A managed long-running task in an ASP.NET Core / generic host |

## Anecdote

In production, the most common abuse of `LongRunning` is: someone wraps an `async Task` in `Task.Factory.StartNew(..., LongRunning)` thinking it'll "use a separate thread." The async method's *first* `await` makes the thread come back; then it's just a normal async method with extra ceremony. Drop `LongRunning`. If you really want isolation, use a custom `TaskScheduler` or simply structure it as a `BackgroundService`.
