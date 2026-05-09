# Cooperative cancellation

There's no preemptive cancellation in modern .NET. Code that wants to be cancellable *cooperates* by checking the token periodically and throwing.

## Designing a cancellable method

| Method shape | What to do |
|---|---|
| Async, calls async APIs | Pass `ct` through; let those APIs throw |
| Async, mostly CPU | Periodically `ct.ThrowIfCancellationRequested()` |
| Sync, mostly CPU | Same — accept `CancellationToken` parameter; periodically throw |
| Sync, blocks on a wait handle | Use `WaitOne(timeout, false)` patterns or `WaitAny(handle, ct.WaitHandle)` |
| Calls a cancellable native API | Bridge with `ct.Register(() => CancelNative())` |

## The "every N iterations" check

```csharp
for (var i = 0; i < items.Length; i++)
{
    if ((i & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
    Process(items[i]);
}
```

Token check is fast but not free. Bunching them avoids a 5–10% throughput loss on tight loops.

## The "branch out of the loop" form

```csharp
foreach (var x in source)
{
    if (ct.IsCancellationRequested) break;
    Process(x);
}
ct.ThrowIfCancellationRequested();
```

Useful when the loop body itself cleans up or you want the loop to finish gracefully on cancel.

## Cancelling synchronous waits

`Thread.Sleep(timeout)` does not honour cancellation. Use `ct.WaitHandle.WaitOne(timeout)` instead — returns `true` if cancelled, `false` on timeout.

```csharp
public static bool CancellableSleep(TimeSpan d, CancellationToken ct)
    => ct.WaitHandle.WaitOne(d);    // true: cancelled; false: slept full duration
```

## Cancelling work that has already started

Cancel doesn't kill. The cancelled work *eventually* finishes (cleanly via the throw-and-bubble-up dance, or never if the work doesn't check). Plan accordingly:

- **Don't cancel work whose side effects are partial-disastrous.** If you've half-written to a file, cancel-mid-write may leave a corrupt file. Guard with transactional writes (write to a temp file, atomic rename).
- **Don't cancel and start a new copy.** The previous copy is still running. You'll have two competing requests until the first throws.
- **Always *await* the cancelled task.** `Task.WhenAll(cancelledTasks)` may throw `OperationCanceledException`; that's *expected*; observe it.

## "Should this method take a token?"

Almost always: yes, if it does any of:

- Calls an async API.
- Has a loop.
- Could plausibly hang.

Default it to `default(CancellationToken)` so callers without one don't have to pass anything:

```csharp
public Task<T> ReadAsync(string id, CancellationToken ct = default)
```

This is now the .NET BCL convention.

## API shape: timeout vs token

For an API that supports cancellation, prefer `(args, CancellationToken)` over `(args, TimeSpan timeout)`:

- The caller can compose multiple sources via linked CTS.
- The caller controls the timeout's lifetime.

If you must take a `TimeSpan`, document whether it's cumulative or per-attempt. Better: take a token and let the caller `CancelAfter`.
