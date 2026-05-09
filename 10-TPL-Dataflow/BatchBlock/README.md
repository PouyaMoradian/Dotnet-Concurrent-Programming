# BatchBlock&lt;T&gt;

Aggregates incoming items and emits them as `T[]` of fixed batch size.

```csharp
var batcher = new BatchBlock<LogEntry>(batchSize: 200);
batcher.LinkTo(writer);          // writer is ActionBlock<LogEntry[]>
```

Excellent for:

- **Database writes** (`INSERT ... VALUES (...)` with N rows per round trip).
- **Network egress** (one HTTP call per batch instead of per item).
- **Telemetry shipping** to backends with batch endpoints.

## `Greedy` mode (default `true`)

Items are accepted as fast as they arrive; the batch closes when full. Some implementations of `BatchBlock` interact with `JoinBlock` and need non-greedy mode for fairness; for linear pipelines, leave it `true`.

## Triggering an early flush

If items arrive slowly and you don't want to wait for `batchSize` items, call:

```csharp
batcher.TriggerBatch();        // emit whatever has been collected, even if < batchSize
```

Production pattern: a timer that calls `TriggerBatch()` every N seconds so the buffer drains on slow days.

```csharp
var timer = new System.Timers.Timer(5000);
timer.Elapsed += (_, _) => batcher.TriggerBatch();
timer.Start();
```

## Bounded batchers

```csharp
var batcher = new BatchBlock<int>(100, new GroupingDataflowBlockOptions { BoundedCapacity = 1000 });
```

`BoundedCapacity` here is the *input* capacity, not the number of pending batches. The block accepts up to that many items before producers must wait.

## Pitfalls

1. **Forgetting to `TriggerBatch` on shutdown** — if the queue has < batchSize items when you call `Complete()`, the partial batch *is* emitted (good). But if there are no items at all, no batch is emitted — make sure your downstream handles "no batches were emitted".
2. **Chaining BatchBlock → BatchBlock**. Usually wrong; pick a final batch size and stick to it.
3. **Memory pressure**: a batch of 1000 items holds 1000 references until the downstream finishes. Size batches to your downstream's throughput, not just the size that "feels nice."
