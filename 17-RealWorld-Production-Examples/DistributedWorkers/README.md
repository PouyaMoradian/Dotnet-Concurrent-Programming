# Distributed workers

A fleet of workers consuming a shared queue (Redis, SQS, Service Bus, RabbitMQ). Each worker is an ASP.NET Core / generic-host process. Concurrency happens at three levels:

1. **Replicas** — multiple worker processes.
2. **Concurrent receivers per worker** — `MaxConcurrency` per consumer.
3. **In-process pipeline** — stages within each item's processing.

## Inside a worker

```csharp
public class QueueWorker(IQueueClient queue, IHandler handler) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Parallel.ForEachAsync(
            queue.ReceiveAsync(ct),                                    // IAsyncEnumerable<Message>
            new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = ct },
            async (msg, innerCt) =>
            {
                try
                {
                    await handler.HandleAsync(msg, innerCt);
                    await queue.AckAsync(msg, innerCt);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    await queue.NackAsync(msg, innerCt);
                    Log.Error(ex, "handler failed for {Id}", msg.Id);
                }
            });
    }
}
```

## Why this shape

- **`Parallel.ForEachAsync`** — declarative concurrency cap; no thread pinning.
- **`MaxDegreeOfParallelism = 16`** — sized to handler latency; tune on real load.
- **Per-message try/catch** — one bad message doesn't kill the worker.
- **Cancellation flowed** — graceful shutdown when `StopAsync` is called.

## Sizing across the fleet

If each worker handles 16 in-flight messages and you have 8 replicas, the cluster handles 128 concurrent. Match that to the downstream's tolerance:

- DB connection pool: 128 connections cluster-wide → fine if your DB allows it.
- External API: 128 concurrent calls → check rate limits.
- CPU / memory: sized for 16 × handler footprint per replica.

## Idempotency

Distributed queues retry on Nack. Handlers **must** be idempotent. Common patterns:

- **Idempotency key** in the handler: store "processed_id" in a fast cache; check before processing.
- **Conditional updates** at the DB level: `UPDATE ... WHERE last_event_id < @incoming`.
- **Append-only event store**: rerunning is a no-op past the watermark.

## Backpressure

The queue *is* the buffer. Slow workers cause the queue to grow → operators alert → scale workers up.

If your queue is in-memory (rare for distributed), reach for `Channel<T>` with a bound.

## Observability

Per-worker metrics:

- Receive rate, ack rate, nack rate.
- Handler latency P50/P95/P99.
- Errors by exception type.

Per-cluster metrics:

- Queue depth.
- Worker count.
- Lag (P99 message age).

Set alerts on lag growth and nack rate.

## Graceful shutdown

```csharp
public override async Task StopAsync(CancellationToken ct)
{
    // base class triggers stoppingToken; our ParallelForEachAsync sees it and stops accepting new messages.
    // In-flight messages complete or get nacked.
    await base.StopAsync(ct);
}
```

Hosting environments (Kubernetes) send SIGTERM and wait `terminationGracePeriodSeconds`. Make sure your shutdown drains within that window.
