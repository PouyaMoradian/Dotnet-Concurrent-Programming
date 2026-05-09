# `Parallel.ForEachAsync` (.NET 6+)

**The right tool for IO-bound fan-out.** It runs an async body for each item, with a configurable concurrency cap, without pinning thread-pool workers across awaits.

## API

```csharp
await Parallel.ForEachAsync(
    items,
    new ParallelOptions
    {
        MaxDegreeOfParallelism = 8,
        CancellationToken = ct,
    },
    async (item, ct) =>
    {
        await ProcessAsync(item, ct);
    });
```

The body must be `Func<T, CancellationToken, ValueTask>` (or `Func<T, CancellationToken, Task>` accepted via implicit conversion in some signatures). The `ct` you receive is *the same token you passed in*; pass it through.

## Why this beats `Parallel.ForEach` over async

- **No thread pinning.** Awaits release the worker. Hundreds of in-flight items, a handful of workers.
- **Native async cancellation.** Cancel propagates to the body via `ct`.
- **Composable with async streams.** Works on `IAsyncEnumerable<T>` since .NET 6.

## What replaces a `SemaphoreSlim` cap

This is exactly the use case that motivated thousands of "fan-out with semaphore" snippets. Now:

```csharp
// before
async Task Old(IEnumerable<Uri> urls)
{
    using var gate = new SemaphoreSlim(8);
    var tasks = urls.Select(async u =>
    {
        await gate.WaitAsync();
        try { return await Fetch(u); }
        finally { gate.Release(); }
    });
    await Task.WhenAll(tasks);
}

// after
async Task New(IEnumerable<Uri> urls) =>
    await Parallel.ForEachAsync(urls,
        new ParallelOptions { MaxDegreeOfParallelism = 8 },
        async (u, ct) => await Fetch(u, ct));
```

## Performance notes

- **`MaxDegreeOfParallelism`** defaults to `ProcessorCount` (which is rarely right for IO — pick based on the upstream service's tolerance, not your CPU).
- **Per-item overhead** is similar to `Parallel.ForEach` — there's no special async penalty.
- **`IAsyncEnumerable<T>` source** is honored for streaming fan-out; you don't have to materialise.

## Pitfalls

1. **Forgetting to pass the `ct`** through to inner async calls. The cap-cancel won't work; failures don't propagate.
2. **Catching exceptions inside the body.** If you swallow them, the loop won't stop. Let them propagate; `Parallel.ForEachAsync` aggregates and rethrows.
3. **Side effects on shared state.** Same rules as ever — protect with locks, atomics, or local accumulators. There's no `localInit`/`localFinally` overload here; aggregate via `ConcurrentBag<T>`/`ConcurrentDictionary<K,V>` or post results to a `Channel<T>`.

## When `Parallel.ForEachAsync` is *not* the right choice

- **Pipeline (multi-stage) fan-out.** Use TPL Dataflow ([Chapter 10](../../10-TPL-Dataflow)) or `Channel<T>` ([Chapter 9](../../09-Channels)).
- **Heavy CPU work disguised as async.** Use `Parallel.ForEach` and offload sync.
- **You need rate-limiting (per-second tokens), not just concurrency cap.** Use `System.Threading.RateLimiting` ([Chapter 16](../../16-Modern-.NET-Features/RateLimiting)).
