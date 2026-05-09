# Linked tokens

When you have multiple sources of cancellation (a parent token, a per-request timeout, a feature flag killswitch), combine them with `CancellationTokenSource.CreateLinkedTokenSource`.

```csharp
using var linked = CancellationTokenSource.CreateLinkedTokenSource(outerCt, timeoutCts.Token, killSwitchCts.Token);
await DoAsync(linked.Token);
```

The linked source fires when **any** of its sources fires. Disposing it unsubscribes from all of them.

## Why dispose matters

Each linked source registers a callback on each of its sources. Without dispose, those registrations accumulate on the long-lived parent tokens — a slow leak that adds up under load.

```csharp
// ❌ leaks if outer lives long
var linked = CancellationTokenSource.CreateLinkedTokenSource(outer, ...);
await DoAsync(linked.Token);
// linked never disposed → outer's registrations grow

// ✅
using var linked = CancellationTokenSource.CreateLinkedTokenSource(outer, ...);
```

## Common pattern: "request scope" cancellation

```csharp
public async Task<Response> HandleAsync(Request req, CancellationToken outer)
{
    using var perRequest = CancellationTokenSource.CreateLinkedTokenSource(outer);
    perRequest.CancelAfter(TimeSpan.FromSeconds(30));   // 30s timeout per request

    return await DoWorkAsync(req, perRequest.Token);
}
```

Either source (outer cancellation or per-request timeout) cancels the work. `perRequest.IsCancellationRequested == true` in both cases; `perRequest.Token == outer` is false (it's the linked one).

## Knowing *which* token fired

Inspect the sources:

```csharp
catch (OperationCanceledException)
{
    if (perRequest.IsCancellationRequested && !outer.IsCancellationRequested)
    {
        // it was the timeout
    }
    else if (outer.IsCancellationRequested)
    {
        // it was the outer (e.g., shutdown)
    }
}
```

For `WaitAsync`-based timeouts, the framework throws `TimeoutException` rather than `OperationCanceledException` — easier to distinguish.

## Performance

`CreateLinkedTokenSource` allocates a new CTS plus one registration per source. ~hundreds of nanoseconds. Don't create one per inner-loop iteration; make it per request.

## Cascading

A linked source can itself be a source for another:

```csharp
using var a = CancellationTokenSource.CreateLinkedTokenSource(outer);
using var b = CancellationTokenSource.CreateLinkedTokenSource(a.Token, perRequest.Token);
```

`b.Token` cancels when outer, perRequest, or `a` itself does. The chain is fine; just make sure every link is disposed.
