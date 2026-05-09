# Structured concurrency

Structured concurrency is a discipline: **child tasks live within the lexical scope of a parent**. When the parent exits, all children are guaranteed to have completed (success, failure, or cancellation). It's the default in Kotlin, Swift, Trio (Python), and proposed for Java's `StructuredTaskScope`. .NET doesn't have a built-in primitive yet but the pattern is straightforward to apply.

## The problem it solves

Without it, fan-out tasks have unclear ownership:

```csharp
// Who cancels these on exception? Who waits for them? What if they leak?
public async Task DoStuffAsync(CancellationToken ct)
{
    var t1 = SomeAsync(ct);
    var t2 = OtherAsync(ct);
    var result = await ProcessAsync(ct);
    // … forgot to await t1, t2 → silent leak
}
```

Structured concurrency says: every task has an *enclosing scope* that knows about it.

## Pattern in modern C#

```csharp
public async Task DoStuffAsync(CancellationToken outer)
{
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);

    var t1 = SomeAsync(cts.Token);
    var t2 = OtherAsync(cts.Token);

    try
    {
        var result = await ProcessAsync(cts.Token);
        await Task.WhenAll(t1, t2);            // wait for siblings before returning
        Use(result, await t1, await t2);
    }
    catch
    {
        cts.Cancel();                          // cancel siblings on failure
        try { await Task.WhenAll(t1, t2); } catch { /* swallow secondary */ }
        throw;
    }
}
```

The key invariant: **on every exit path** (success, exception, cancellation), siblings are awaited. No fire-and-forget.

## A reusable helper

```csharp
public sealed class TaskGroup : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly List<Task> _tasks = new();

    public CancellationToken Token => _cts.Token;

    public TaskGroup(CancellationToken parent)
        => _cts = CancellationTokenSource.CreateLinkedTokenSource(parent);

    public Task Run(Func<CancellationToken, Task> work)
    {
        var t = work(_cts.Token);
        lock (_tasks) _tasks.Add(t);
        return t;
    }

    public async ValueTask DisposeAsync()
    {
        Task[] snapshot;
        lock (_tasks) snapshot = _tasks.ToArray();

        try { await Task.WhenAll(snapshot); }
        catch
        {
            _cts.Cancel();
            try { await Task.WhenAll(snapshot); } catch { /* observed */ }
            throw;
        }
        finally { _cts.Dispose(); }
    }
}
```

Use:

```csharp
await using var group = new TaskGroup(ct);
var t1 = group.Run(SomeAsync);
var t2 = group.Run(OtherAsync);
await t1; await t2;
```

If the body throws, `DisposeAsync` cancels and awaits siblings before unwinding.

## Best-effort vs all-or-nothing

Two modes:

1. **All-or-nothing**: any failure cancels the rest. The pattern above.
2. **Best-effort**: collect successes, log failures. Use `Task.WhenAll` and inspect each task's `.Exception`.

For request handlers, all-or-nothing is usually right (a partial result is misleading). For batch workers, best-effort is often right (one bad item shouldn't kill the batch).

## When .NET will catch up

There's an active proposal for a built-in `TaskScope` / `TaskGroup` in `System.Threading.Tasks`. Until then, the helper above is the standard pattern. Keep it in your shared utilities; reach for it whenever you have more than two concurrent async children.
