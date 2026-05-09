# ExecutionContext

`ExecutionContext` is "the ambient information that flows across `await`s and `Task.Run`/`ThreadPool.QueueUserWorkItem` boundaries." It includes:

- **`AsyncLocal<T>` values** — the canonical user-facing API.
- **Impersonation token** (Windows; rarely used in modern code).
- **Some security state.**
- **OpenTelemetry / Diagnostic activity context** (rides on AsyncLocal under the hood).

It does **not** include:

- `SynchronizationContext` (separate concept; see neighbour folder).
- `ThreadStatic` fields (those don't flow).
- `CallContext` (.NET Framework legacy; replaced by AsyncLocal).

## What flows where

| Mechanism | Flows ExecutionContext? |
|---|---|
| `await` on Task | yes (default) |
| `Task.Run(...)` | yes |
| `ThreadPool.QueueUserWorkItem(...)` | yes |
| `ThreadPool.UnsafeQueueUserWorkItem(...)` | **no** ("Unsafe" = no flow) |
| `new Thread(...).Start()` | no |
| `Timer` callbacks | yes |
| `IObserver<T>.OnNext` | depends on producer |

The "Unsafe" suffix on ThreadPool methods means "I'm explicitly opting out of EC flow." Use it when you have profiled the overhead and don't need flow.

## `AsyncLocal<T>` 101

```csharp
private static readonly AsyncLocal<string?> CorrelationId = new();

public async Task HandleAsync(Request req)
{
    CorrelationId.Value = req.Id;
    await DoStuffAsync();          // flows: DoStuffAsync sees CorrelationId == req.Id
    Log.Info(...);                  // flows
}
```

The flow happens on **every async-machinery transition** that captures EC. The cost is "a few hundred ns per await that suspends" and "one EC clone when the value mutates".

## The change-notification callback

Sometimes useful, often confusing:

```csharp
new AsyncLocal<int>(args =>
{
    Console.WriteLine($"context changed: thread={Environment.CurrentManagedThreadId} prev={args.PreviousValue} curr={args.CurrentValue} flow={args.ThreadContextChanged}");
});
```

The callback fires when the value changes due to flow (a continuation resuming, a callback firing). Used by some logging libraries to refresh thread-local mirrors.

## Performance

Each EC capture is a copy-on-write snapshot. Common operations:

- **Read AsyncLocal<T>**: ~3 ns (effectively a struct field read on the current EC).
- **Write AsyncLocal<T>**: ~30–50 ns (creates a new EC and updates `Thread.CurrentExecutionContext`).
- **Capture for await**: ~10 ns (copy the reference).
- **Restore on resume**: ~10 ns (set `Thread.CurrentExecutionContext`).

If you're allocating thousands of awaits per request, the EC churn is real. Mitigations:

- Don't write AsyncLocal in hot loops; set once near the request boundary.
- Use `ThreadPool.UnsafeQueueUserWorkItem` for intentionally-isolated work.
- Use `ExecutionContext.SuppressFlow()` / `RestoreFlow()` around code that explicitly shouldn't flow context (rare).

## Antipatterns

1. **Putting mutable state in AsyncLocal.** EC is *snapshot*-flowed: child contexts see the parent's value at capture time, not later updates. AsyncLocal is for read-mostly request-scoped data.
2. **Confusing AsyncLocal with ThreadStatic.** ThreadStatic is per-thread and *doesn't* flow. AsyncLocal is per-async-flow and *does*. They look similar; they aren't.
3. **Storing IDisposable in AsyncLocal.** No clear ownership; nobody calls Dispose. Use scope objects or DI.
