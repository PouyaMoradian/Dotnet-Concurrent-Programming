# Timeout patterns

Time-bound async operations are everywhere. Three primitives, three appropriate moments.

## 1. `CancellationTokenSource` with `CancelAfter`

Best when you want a single token that combines parent cancellation with the timeout, and you want the operation to *cooperate* (i.e., its inner code checks the token and stops).

```csharp
using var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
cts.CancelAfter(TimeSpan.FromSeconds(5));
await DoAsync(cts.Token);
```

If the operation throws `OperationCanceledException` and the timeout fired, you can detect that:

```csharp
catch (OperationCanceledException) when (cts.IsCancellationRequested && !outer.IsCancellationRequested)
{
    throw new TimeoutException();
}
```

## 2. `Task.WaitAsync(TimeSpan, CancellationToken)` — .NET 6+

When you have a task you can't otherwise cancel and you just want to *give up waiting*:

```csharp
try { var result = await task.WaitAsync(TimeSpan.FromSeconds(5)); }
catch (TimeoutException) { /* the task is still running, but we stopped waiting */ }
```

**Caveat:** the underlying task is *not* cancelled. If you spawned it with `Task.Run(() => ExpensiveSync())`, the work continues and consumes a thread. Use this only when you're prepared to let the task be observed later (or you accept the leaked thread for the rest of its lifetime).

## 3. `Task.WhenAny(task, Task.Delay(timeout))` — pre-.NET 6

```csharp
var t = Task.Delay(timeout);
var winner = await Task.WhenAny(task, t);
if (winner == t) throw new TimeoutException();
return await task;   // task already finished → no extra wait
```

`Task.WaitAsync` superseded this. The pattern is everywhere in older codebases; refactor when you find one.

## Combined pattern: timeout + retry

```csharp
for (var attempt = 1; attempt <= maxAttempts; attempt++)
{
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
    cts.CancelAfter(perAttemptTimeout);
    try
    {
        return await DoAsync(cts.Token);
    }
    catch (OperationCanceledException) when (!outer.IsCancellationRequested && attempt < maxAttempts)
    {
        await Task.Delay(BackoffMs(attempt), outer);
        continue;
    }
}
throw new TimeoutException("all retries exhausted");
```

For non-trivial retry logic, **use `Polly`** (NuGet). Hand-rolled retry is rarely as good as Polly's tested implementation. See `14-Advanced-Patterns/CircuitBreakers`.

## Pitfalls

1. **Reusing one CTS across retries** — a cancelled CTS stays cancelled. Always create a fresh linked CTS per attempt.
2. **Linking the parent's cancellation but not propagating it** — the parent token's cancellation should *not* be retried; check `outer.IsCancellationRequested` before retry.
3. **Mixing `Task.WaitAsync` with shared resources** — if the task held a connection that's now leaked, the timeout caused a resource leak. Sync the cleanup with the task.
