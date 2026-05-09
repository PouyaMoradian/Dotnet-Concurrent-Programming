# High-throughput pipelines

Ingest-heavy systems (telemetry, log shipping, message processing) live and die by the throughput of their inner pipeline. `Channel<T>` plus careful attention to allocations gets you tens of millions of items/second on a single box.

## The shape

A pipeline is a chain of stages, each with its own input channel:

```
[ Source ] → ch1 → [ Decode ] → ch2 → [ Enrich ] → ch3 → [ Sink ]
```

Each stage is its own `Task` reading `await foreach (var x in chN.Reader.ReadAllAsync())`. Channels carry items between stages. Each stage's output channel is the next stage's input.

## Setting up for speed

### 1. Use SPSC channels where applicable

If a stage has exactly one writer and one reader, set both hints:

```csharp
Channel.CreateBounded<T>(new BoundedChannelOptions(1024)
{
    SingleReader = true, SingleWriter = true,
});
```

The implementation can use a faster code path. Often a 2× throughput improvement vs MPMC channels.

### 2. Avoid per-item allocations in the body

Pre-allocate buffers, reuse `StringBuilder`s in `[ThreadStatic]` slots, use `ArrayPool<T>.Shared.Rent`. The state machine for `await foreach` may already allocate; minimise *additional* allocations.

### 3. Batch where downstream allows

Rather than one-item-at-a-time, batch into `IList<T>` of N items. Each stage processes the batch as a unit.

```csharp
var batches = Channel.CreateBounded<List<T>>(8);
// stage A: collect 256 items into a list, write the list
// stage B: receive the list, process all of them
```

Per-stage overhead amortises across the batch.

### 4. Pin or partition CPU-bound stages

If a stage is CPU-bound, run multiple readers in parallel (relax `SingleReader`):

```csharp
var ch = Channel.CreateBounded<T>(new BoundedChannelOptions(1024) { SingleWriter = true /* SingleReader = false */ });

for (var i = 0; i < Environment.ProcessorCount; i++)
{
    _ = Task.Run(async () =>
    {
        await foreach (var x in ch.Reader.ReadAllAsync())
            Process(x);          // CPU-bound; many workers
    });
}
```

### 5. Measure, then optimise

```csharp
[Benchmark]
public async Task Pipeline()
{
    var ch = Channel.CreateBounded<int>(1024);
    var consumer = Task.Run(async () =>
    {
        long s = 0;
        await foreach (var x in ch.Reader.ReadAllAsync()) s += x;
        return s;
    });
    for (var i = 0; i < N; i++) await ch.Writer.WriteAsync(i);
    ch.Writer.Complete();
    await consumer;
}
```

Compare with `BlockingCollection<int>`, `ConcurrentQueue<int> + ManualResetEventSlim`, and `Channel<int>(SingleReader, SingleWriter)`. The channel version is typically the winner.

## Common throughput pitfalls

| Symptom | Cause |
|---|---|
| GC churn | Per-item allocations in stage bodies |
| One core pinned, others idle | Single-reader stage with CPU-bound work |
| All cores busy, throughput flat | Lock contention somewhere — likely outside the channel |
| Latency spikes | A stage's batching/buffering hides progress under load |

## Real-world cases handled this way

- **Application telemetry pipelines** — collector → batcher → exporter.
- **Kafka consumer pipelines** — consume → deserialise → enrich → write.
- **Image / video ingestion** — reader → decoder workers → encoder workers → sink.

The pattern generalises: split into stages, channels between, bound everything, parallelise the bottleneck stage.
