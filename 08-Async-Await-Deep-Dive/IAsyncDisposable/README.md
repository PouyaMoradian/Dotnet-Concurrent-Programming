# `IAsyncDisposable` and `await using`

When `Dispose` itself needs to do async work (flush a buffer, send a final frame, gracefully close a connection), it can't be `void`. `IAsyncDisposable.DisposeAsync()` returns a `ValueTask`.

## Definition

```csharp
public sealed class FlushableLogger : IAsyncDisposable
{
    private readonly Channel<string> _q = Channel.CreateUnbounded<string>();
    private readonly Task _pump;

    public FlushableLogger(Func<string, Task> sink)
        => _pump = Task.Run(async () =>
        {
            await foreach (var line in _q.Reader.ReadAllAsync())
                await sink(line);
        });

    public ValueTask LogAsync(string s) { _q.Writer.TryWrite(s); return default; }

    public async ValueTask DisposeAsync()
    {
        _q.Writer.Complete();
        await _pump;
    }
}
```

Use:

```csharp
await using var logger = new FlushableLogger(SendToBackendAsync);
await logger.LogAsync("hello");
// dispose flushes and waits for pump to finish
```

## Implementing both `IDisposable` and `IAsyncDisposable`

If your type can be used in both sync and async contexts, implement both. The standard pattern:

```csharp
public class MyResource : IDisposable, IAsyncDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        DisposeCore();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await DisposeAsyncCore();
        DisposeCore();              // sync part: handles, etc.
        GC.SuppressFinalize(this);
    }

    protected virtual ValueTask DisposeAsyncCore() => default;
    protected virtual void DisposeCore() => _disposed = true;
}
```

Subclasses override the `Core` methods.

## When `IDisposable` is enough

If your `Dispose` does only sync work (closes a handle, marks a flag), use `IDisposable`. Don't over-implement `IAsyncDisposable` for ceremony. The two operations are not interchangeable; consumers pick based on their context.

## Using-scope semantics

`await using` translates to:

```csharp
{
    var resource = expr;
    try { /* body */ }
    finally
    {
        if (resource != null) await resource.DisposeAsync().ConfigureAwait(false);
    }
}
```

Note `ConfigureAwait(false)` — yes, that one is implicit. The compiler inserts it. So the resumption thread of the implicit dispose is whichever pool worker finishes the `DisposeAsync` task. If you need the body's continuation on a specific context, bracket explicitly.

## Pitfall: forgetting `await`

```csharp
// ❌ silently fire-and-forget
using var resource = expr;        // sync using on an IAsyncDisposable
```

If `expr` returns something that implements **only** `IAsyncDisposable`, the compiler won't allow `using` (no `Dispose`). If it implements **both**, `using` calls `Dispose` and the async cleanup is skipped. Always `await using` for `IAsyncDisposable`.
