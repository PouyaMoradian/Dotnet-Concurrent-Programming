# ActionBlock&lt;T&gt;

A terminal block — runs a delegate per item, no output. Common as the last stage of a pipeline.

```csharp
var sink = new ActionBlock<int>(
    async x => await SaveAsync(x),
    new ExecutionDataflowBlockOptions
    {
        MaxDegreeOfParallelism = 4,
        BoundedCapacity = 100,
        SingleProducerConstrained = false,
    });
```

## When the action throws

Exceptions in the delegate fault the block. The `Completion` task becomes faulted with an `AggregateException`. **Always observe `Completion`.**

```csharp
try { await sink.Completion; }
catch (Exception ex) { _logger.LogError(ex, "sink failed"); throw; }
```

Without observing, you get a fault that's invisible to anything upstream that's still posting (until they post into a faulted block, which silently drops).

## `SingleProducerConstrained`

If you guarantee only one task ever calls `Post`/`SendAsync`, set `SingleProducerConstrained = true` for a faster code path. Set it to `false` if you have multiple producers (default).

## Pattern: ActionBlock as a *bounded async work queue*

Standalone, an ActionBlock makes a great "submit work, bounded concurrency, awaitable completion" primitive:

```csharp
public sealed class WorkQueue<T>
{
    private readonly ActionBlock<T> _block;

    public WorkQueue(Func<T, Task> handler, int maxParallel, int bound = 1000)
        => _block = new ActionBlock<T>(handler, new()
        {
            MaxDegreeOfParallelism = maxParallel,
            BoundedCapacity = bound,
        });

    public ValueTask Submit(T item) => new(_block.SendAsync(item));
    public Task Drain() { _block.Complete(); return _block.Completion; }
}
```

This is more ergonomic than rolling `SemaphoreSlim` + `Task.Run` for a fan-out worker pool.

## Comparison with `Channel<T>` + worker tasks

| | `ActionBlock<T>` | `Channel<T>` + workers |
|---|---|---|
| Per-item allocation | higher | lower |
| Concurrency cap | built-in (`MaxDegreeOfParallelism`) | manual (worker count) |
| Backpressure | built-in (`BoundedCapacity`) | built-in (`Channel.CreateBounded`) |
| Predicate routing | n/a (terminal) | manual |
| Composition | LinkTo from upstream blocks | manual `await foreach` chains |

For a lone "submit work" sink, `ActionBlock<T>` is concise. For a multi-stage pipeline, you'll likely use multiple Dataflow blocks anyway and the consistency is worth the small per-item cost.
