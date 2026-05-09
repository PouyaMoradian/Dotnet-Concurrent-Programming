# TransformBlock&lt;TIn, TOut&gt;

The workhorse stage: input → transform delegate → output. Configurable parallelism per block.

```csharp
var fetch = new TransformBlock<Uri, string>(
    async u => await client.GetStringAsync(u),
    new ExecutionDataflowBlockOptions
    {
        MaxDegreeOfParallelism = 8,
        BoundedCapacity = 64,
        EnsureOrdered = false,           // allow out-of-order outputs for throughput
    });
```

## Important options

| Option | Effect |
|---|---|
| `MaxDegreeOfParallelism` | how many transforms run concurrently |
| `BoundedCapacity` | input + output buffer combined cap |
| `EnsureOrdered` | output preserves input order (default `true`); `false` lets faster items overtake |
| `CancellationToken` | cancels the block |
| `TaskScheduler` | which scheduler runs the delegate |

## `EnsureOrdered` — the surprising one

By default, `TransformBlock` emits outputs in **input order**, even when the delegate runs concurrently. To do this, it has to buffer outputs that finished early until their predecessors finish. This is fine for low concurrency; for `MaxDegreeOfParallelism > 1` and tight downstream queues it can cause unexpected stalls.

`EnsureOrdered = false` lets fast items overtake slow ones. Use it when downstream doesn't care about order (e.g., aggregating into a sum).

## TransformManyBlock

A variant where the delegate returns `IEnumerable<TOut>` (or `Task<IEnumerable<TOut>>`). Each input becomes 0..N outputs. Useful for "split each line into words", "fan-out per record into per-field events".

## Anti-patterns

1. **Capturing per-call state in closures.** With `MaxDegreeOfParallelism > 1`, the same delegate runs in parallel — closure-captured state is racy.
2. **Throwing from the delegate without observing.** A faulted block doesn't propagate to upstream automatically; you must inspect `Completion`.
3. **Using `TransformBlock` for tiny work.** The per-item overhead (a few hundred ns) dwarfs sub-microsecond work. Use `Parallel.For`/`Channel<T>` instead.
