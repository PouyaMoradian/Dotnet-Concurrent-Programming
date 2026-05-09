# `ValueTask` and `ValueTask<TResult>`

A struct-based alternative to `Task<TResult>` for hot async paths where the result is *frequently available synchronously* (cache hits, fast paths, etc.).

## The allocation argument

`async Task<T>` allocates:

1. The state machine box (one object per call that suspends).
2. The `Task<T>` itself (one object per call).

`async ValueTask<T>` whose body completes synchronously allocates **zero** of these. When the body does suspend, it falls back to a normal Task-backed completion. The trade-off: more rules of usage, smaller surface area.

## The hot-path pattern

```csharp
public ValueTask<byte[]> GetAsync(string key)
{
    if (_cache.TryGetValue(key, out var cached))
        return new ValueTask<byte[]>(cached);     // sync completion, zero allocation

    return new ValueTask<byte[]>(SlowAsync(key));  // wraps a Task; rare path
}

private async Task<byte[]> SlowAsync(string key) { /* ... */ }
```

For high-frequency cache-style APIs (e.g., parser fast paths, expression evaluators, streaming readers) this is the difference between gen0 GC pressure and none.

## The rules

A `ValueTask<T>` may **only be consumed once**. Specifically:

- **Don't `await` twice.**
- **Don't `await` it from multiple consumers.**
- **Don't store it in a field for later.**

If you need any of those, **convert to `Task` once via `.AsTask()`**, then use the `Task` freely. Or just use `Task<T>` directly.

## When NOT to use ValueTask

- **Public APIs where consumers might double-await** — `Task<T>` is safer.
- **Methods that almost never complete synchronously** — the wrapper costs more than the saved allocation.
- **Generic abstractions with library-level expectations** — many libraries built around `Task<T>` will materialise to `Task` anyway.

## Custom builders for pooling

`[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]` (since .NET 7) instructs the compiler to use a pooled state machine box. Combined with `ValueTask`, this gives you allocation-free async even on the *suspending* path:

```csharp
[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<int>))]
public static async ValueTask<int> ReadAsync()
{
    await Task.Yield();
    return 42;
}
```

Or, process-wide, set `DOTNET_SYSTEM_THREADING_POOLASYNCVALUETASKS=1`. See [08/AllocationFreeAsync](../../08-Async-Await-Deep-Dive/AllocationFreeAsync) for the full picture.

## Async iteration with ValueTask

`IAsyncEnumerable<T>.MoveNextAsync()` returns `ValueTask<bool>`. The async iteration protocol is built around `ValueTask` precisely so that synchronous-completion items (cached, in-buffer) don't allocate. Custom `IAsyncEnumerable<T>` implementations should preserve the property by yielding from caches synchronously where possible.

## Migration path

For most server code: leave `Task<T>` alone. Migrate to `ValueTask<T>` for measured hot paths (a benchmark showing > 5% allocation reduction with no perf regression).
