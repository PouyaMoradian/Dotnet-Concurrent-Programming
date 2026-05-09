# Pipelines

A pipeline is a chain of stages, each transforming items from a queue and forwarding them to the next. We've covered three implementations:

- **`Channel<T>` chains** — [09/HighThroughputPipelines](../../09-Channels/HighThroughputPipelines/).
- **TPL Dataflow** — [10-TPL-Dataflow](../../10-TPL-Dataflow/).
- **System.IO.Pipelines** — for byte-level network protocols.

This README is about *choosing*.

## Decision matrix

| Need | Best |
|---|---|
| Few stages, simple types, max throughput | `Channel<T>` |
| Many stages with different parallelism each | TPL Dataflow |
| Predicate routing | TPL Dataflow (`LinkTo` predicate) |
| Network protocol parsing | `System.IO.Pipelines` |
| Pull-based stream | `IAsyncEnumerable<T>` |
| Push-based event composition | `IObservable<T>` (Rx) |

## A pipeline checklist

For any pipeline you build:

1. **Each stage has its own bounded queue.** No unbounded queues anywhere.
2. **Each stage's parallelism is sized to its bottleneck.** Don't run a 1-thread DB writer at `ProcessorCount`.
3. **Cancellation flows through every stage.** Each `await` takes the same `CancellationToken`.
4. **Completion propagates.** Closing the source eventually drains and closes the sink.
5. **Errors propagate.** A faulted stage is observable from the sink; the pipeline doesn't silently drop messages.
6. **Telemetry per stage.** Queue length and processing rate.
7. **Graceful shutdown.** Stop accepting, drain, then dispose.

## Anti-patterns

- **A "pipeline" of `Task.Run` chains** without bounding. Looks fine until production traffic.
- **Pipeline with hidden synchronisation between stages**. Each stage should only depend on its input queue.
- **Mixing `Task.WaitAll` and `Channel.Reader.Completion`**. Pick one notion of "I'm done".

## A common shape

```csharp
public sealed class Pipeline<TInput> : IAsyncDisposable
{
    private readonly Channel<TInput> _ingress;
    private readonly Task _runner;
    private readonly CancellationTokenSource _cts = new();

    public Pipeline(int capacity, Func<IAsyncEnumerable<TInput>, CancellationToken, Task> body)
    {
        _ingress = Channel.CreateBounded<TInput>(capacity);
        _runner = Task.Run(() => body(_ingress.Reader.ReadAllAsync(_cts.Token), _cts.Token));
    }

    public ValueTask SendAsync(TInput item, CancellationToken ct = default)
        => _ingress.Writer.WriteAsync(item, ct);

    public async ValueTask DisposeAsync()
    {
        _ingress.Writer.Complete();
        try { await _runner; }
        finally { _cts.Dispose(); }
    }
}
```

Reusable across applications. Each `body` defines the pipeline's stages over the input enumerable.
