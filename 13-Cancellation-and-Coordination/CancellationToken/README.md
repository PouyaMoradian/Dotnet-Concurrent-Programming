# CancellationToken

A read-only struct that exposes:

- `IsCancellationRequested` — boolean snapshot.
- `ThrowIfCancellationRequested()` — throws `OperationCanceledException` if requested.
- `Register(Action callback)` — register a callback to fire on cancel.
- `WaitHandle` — for interop with sync APIs that take a `WaitHandle`.

## Idioms

### In CPU loops

```csharp
public int CountPrimes(int n, CancellationToken ct)
{
    var count = 0;
    for (var i = 2; i <= n; i++)
    {
        if ((i & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();   // every ~64K iters
        if (IsPrime(i)) count++;
    }
    return count;
}
```

Don't check on every iteration — token check is fast (a load), but a hot loop sees significant overhead. Check periodically.

### In async methods

Pass the token to every async call. The cancellable APIs do the right thing — `Task.Delay(ms, ct)` throws `OperationCanceledException(ct)` if cancelled.

```csharp
public async Task DoAsync(CancellationToken ct)
{
    await Task.Delay(100, ct);
    var data = await client.GetByteArrayAsync(url, ct);
    await ProcessAsync(data, ct);
}
```

### Bridging events

```csharp
ct.Register(() => server.Close());     // close socket on cancel
ct.Register(state => ((HttpClient)state).Dispose(), client);   // avoid alloc-on-cancel
```

The `Register` callback fires on whichever thread calls `Cancel()`. Don't do anything heavy or blocking; post to the pool if needed.

## `CancellationToken.None`

The "never cancels" token. Use as a default when an API requires the parameter and the caller has no token. Equivalent to `default(CancellationToken)`.

## `Register` returns a `CancellationTokenRegistration`

Dispose it to unregister:

```csharp
using var registration = ct.Register(() => Cancel());
// ... do work ...
// registration disposed at end of scope: callback removed
```

For long-lived tokens with many transient registrations, dispose to avoid memory growth.

## `Cancellation.Token.WaitHandle`

A lazy-initialised `WaitHandle` for interop with `WaitHandle.WaitAny`/`WaitOne` patterns. Useful for old code; new code should prefer async.

## Patterns to learn

```csharp
// Convert any awaitable to "wait at most this long"
async Task<T> WithTimeout<T>(Task<T> t, TimeSpan timeout, CancellationToken outer = default)
    => await t.WaitAsync(timeout, outer);

// Convert any awaitable to "cancel at this token"
async Task<T> WithCancel<T>(Task<T> t, CancellationToken ct)
    => await t.WaitAsync(ct);
```

`Task.WaitAsync(...)` (.NET 6+) is the right primitive for both. It does *not* cancel the underlying task — it just stops *waiting*. The underlying task keeps running in the background; if you need it to actually stop, you need cooperative cancellation inside it.
