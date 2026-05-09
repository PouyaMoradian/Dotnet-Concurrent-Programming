# 17 — Real-World Production Examples

> **Layer:** application architecture
> **Reading time:** ~30 minutes

End-to-end shapes from real production systems, mapped to the primitives we've covered.

## In-chapter folders

| Folder | What it shows |
|---|---|
| [HighFrequencyTrading](HighFrequencyTrading/) | Pinned threads, allocation-free hot paths, lock-free ring buffers |
| [DistributedWorkers](DistributedWorkers/) | Many-worker BackgroundService consuming a shared queue |
| [API-Gateways](API-Gateways/) | ASP.NET Core, rate limiting, bulkheads, circuit breakers |
| [KafkaConsumers](KafkaConsumers/) | Pacing the broker via async, partition-aware concurrency |
| [SignalR](SignalR/) | Many-client realtime; backpressure on the server |
| [TelemetryPipelines](TelemetryPipelines/) | Channel-based collector → exporter |
| [BackgroundServices](BackgroundServices/) | The right shape for `IHostedService` workers |

## Common motifs

Across these, you'll see:

1. **One bounded queue per stage.** Backpressure propagates.
2. **Per-tenant or per-shard partitioning.** A noisy tenant doesn't take down the rest.
3. **Cancellation flowed end-to-end.** No `Task.Run` swallows the token.
4. **Telemetry on the queue lengths and processing rates.** Observable saturation.
5. **Graceful shutdown.** Stop accepting; drain with deadline; force-cancel after.

The systems that don't follow these patterns *eventually* fail under load. The systems that do, scale.

## Run

The chapter's `Program.cs` runs a couple of representative shapes (a worker service and a small Kafka-shaped consumer). The full systems are too big to fit in one demo; the README files describe them.

```bash
dotnet run --project 17-RealWorld-Production-Examples
```
