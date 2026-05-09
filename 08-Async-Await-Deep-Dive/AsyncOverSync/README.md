# Async over sync — wrapping blocking work in `Task.Run`

Sometimes you have a synchronous API but need to call it from an async method without blocking the caller. `Task.Run(() => SlowSync())` is the tool. It's also frequently misused.

## When it's the right move

- **CPU-heavy computation** in an ASP.NET Core handler that you don't want to occupy the request thread for. Offload to a pool worker.
- **Legacy library** with no async API that you're calling occasionally.
- **Migrating gradually**: synchronous code path now, async API later.

```csharp
public async Task<Stats> ComputeAsync(byte[] image, CancellationToken ct)
{
    return await Task.Run(() => HeavyImageProcessor.Analyse(image, ct), ct);
}
```

## When it's a *lie*

- **You expose `Task<T> DoAsync()` that's implemented as `Task.Run(() => Sync())`.** Callers think they're getting async; they're getting a sync method on a different thread. The cost (a worker thread for the duration) is unchanged, just relocated. Bad in libraries; misleading in any production API.
- **You wrap to "make IO async" without changing the IO API.** `Task.Run(() => File.ReadAllText(path))` doesn't free a thread — the thread is just *another* pool thread, blocked. Use `File.ReadAllTextAsync` (which uses `FileStream` with `useAsync: true` underneath).

## The right rule

> **Async is a leaf concept.** It must be implemented at the lowest level (the IO call, the wait primitive). Wrapping a sync method in `Task.Run` doesn't make the work async; it just moves the blocking elsewhere.

## When you must offload

If you're in an async method and need to call a slow sync function, three options:

1. **`Task.Run(...)`** — hands off to the pool. Caller's thread is freed. Simplest.
2. **Custom `TaskScheduler`** — for sustained work, you might want a dedicated scheduler so it doesn't perturb pool hill-climbing.
3. **`new Thread(...)`** — for indefinite work outside the pool entirely.

For most apps, (1) is right.

## CancellationToken in `Task.Run`

`Task.Run(() => Work(ct), ct)`:

- The outer `ct` cancels the *queued* task before it starts.
- The inner `ct` (passed to `Work`) is what cancels the work itself, **if** `Work` cooperates.

If `Work` is a CPU loop that never checks `ct`, `Task.Run`'s ct can't help. The token must be honoured inside the body.

## Pool starvation risk

Wrapping every slow call in `Task.Run` *moves* the cost; it doesn't eliminate it. If your service has 100 concurrent requests each calling `Task.Run(() => SlowSync())`, you've now got 100 pool workers blocked. The pool grows; latency spikes. Fix the underlying call to be properly async.

## Alternatives

| Situation | Better |
|---|---|
| Slow IO with sync API | Use the async overload (`*Async`); make sure the underlying handle is async |
| Slow CPU work | Acceptable; possibly use `Parallel.For` inside if it's parallelisable |
| Recursive async-over-sync-over-async | Untangle. The middle sync layer is the bug |
