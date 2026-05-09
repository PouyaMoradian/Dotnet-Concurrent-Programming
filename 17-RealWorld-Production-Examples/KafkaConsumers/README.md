# Kafka consumers

Kafka consumers are a textbook concurrency exercise: one consumer instance per partition, each processing in order, but multiple instances scaling out via the consumer group protocol.

## Per-partition concurrency model

Inside one consumer instance, partitions are independent. Two reasonable patterns:

### Pattern A: parallel poll, sequential per-partition processing

```csharp
var consumer = new ConsumerBuilder<string, string>(config).Build();
consumer.Subscribe("topic");

while (!ct.IsCancellationRequested)
{
    var batch = consumer.Consume(TimeSpan.FromMilliseconds(100));
    if (batch is null) continue;

    // sequential per partition by virtue of single-threaded processing
    await ProcessAsync(batch, ct);
    consumer.Commit(batch);
}
```

Simple, in-order, low concurrency.

### Pattern B: parallel processing per partition (bounded)

```csharp
var perPartition = new Dictionary<TopicPartition, Channel<ConsumeResult<string, string>>>();

while (!ct.IsCancellationRequested)
{
    var msg = consumer.Consume(TimeSpan.FromMilliseconds(100));
    if (msg is null) continue;
    var ch = perPartition.GetOrAdd(msg.TopicPartition, tp =>
    {
        var c = Channel.CreateBounded<ConsumeResult<string, string>>(64);
        _ = Task.Run(async () =>
        {
            await foreach (var item in c.Reader.ReadAllAsync(ct))
            {
                await ProcessAsync(item);
                consumer.StoreOffset(item);   // marks for next commit
            }
        });
        return c;
    });
    await ch.Writer.WriteAsync(msg, ct);
}
```

Each partition has its own queue and worker. Within a partition: in-order. Across partitions: parallel.

The `Channel<T>(64)` bound provides backpressure; the `Consume` poll waits for a free slot before pulling more from the broker.

## Commit semantics

Two modes:

- **At-least-once**: commit *after* successful processing. Duplicates possible on crash.
- **Effectively-once**: write to a transactional sink (DB transaction + commit offset together). Most production code aims for at-least-once + idempotent handlers.

Don't auto-commit. Auto-commit can ack messages you haven't yet processed if the worker crashes.

## Backpressure across the broker

If your worker is slow and the partition queue fills, the consumer's `Consume` blocks. The broker maintains the offset; you don't lose messages. The consumer group's *lag* metric grows — that's how you observe saturation.

## Pause / resume

For tighter control, `consumer.Pause(...)` / `consumer.Resume(...)` lets the consumer hold off polling without leaving the group. Useful for "this partition is in error state; back off for a minute."

## Rebalance handling

When partitions move, in-flight processing must be drained or aborted. Patterns:

- **Drain** on revocation: complete current messages; commit; rebalance can proceed.
- **Abandon** on revocation: cancel processing; the new owner will replay (relies on idempotency).

The Confluent .NET client's `IConsumer<TKey, TValue>` exposes `SetPartitionsRevokedHandler` for this.

## Observability

- Consumer lag per partition.
- Processing rate.
- Handler latency.
- Commit success / failure rates.

Confluent's client emits metrics; OpenTelemetry's `Confluent.Kafka.Diagnostics` package wraps them.

## Cancellation

The whole pipeline must respect a single `CancellationToken` (the host's `stoppingToken` in `BackgroundService.ExecuteAsync`). `consumer.Consume(timeout)` honours short timeouts; structure your loop to check `ct.IsCancellationRequested` between calls.
