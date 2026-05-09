# 09 — Channels (`System.Threading.Channels`)

> **Layer:** BCL
> **Reading time:** ~25 minutes
> **Prereq:** [04](../04-Synchronization-Primitives/), [08](../08-Async-Await-Deep-Dive/)

`Channel<T>` is the right answer for almost any "producer / consumer with backpressure" need in modern .NET. It's faster, smaller-allocation, and async-friendlier than `BlockingCollection<T>` — and the API is the most ergonomic in the BCL.

## Mental model

A channel is a thread-safe pipe with two ends:

- A `Writer` (one or more producers).
- A `Reader` (one or more consumers).

Items go in via `WriteAsync`, come out via `ReadAsync` / `ReadAllAsync`. Bounded channels apply backpressure when full; unbounded channels grow indefinitely.

## API surface

```csharp
var ch = Channel.CreateBounded<int>(new BoundedChannelOptions(1024)
{
    SingleReader = true,                   // optimisation hint
    SingleWriter = true,                   // optimisation hint
    FullMode = BoundedChannelFullMode.Wait,// or DropOldest, DropNewest, DropWrite
    AllowSynchronousContinuations = false, // safe default
});

await ch.Writer.WriteAsync(42);
var x = await ch.Reader.ReadAsync();
ch.Writer.Complete();                      // close — readers see end-of-stream
await foreach (var item in ch.Reader.ReadAllAsync()) { /* … */ }
```

## In-chapter folders

| Folder | Topic |
|---|---|
| [BoundedChannels](BoundedChannels/) | Backpressure, FullMode options, sizing |
| [UnboundedChannels](UnboundedChannels/) | When unbounded is right (rare); when it's a leak (common) |
| [Backpressure](Backpressure/) | Producers slowing for consumers — the whole point of bounded |
| [HighThroughputPipelines](HighThroughputPipelines/) | Multi-stage pipelines with channels; SPSC tuning |
| [ActorPatterns](ActorPatterns/) | One reader, one mailbox: thread-safe state without locks |

## Sizing

| Bound | When |
|---|---|
| 1 | "rendezvous" — producer waits for consumer; useful for handoff guarantees |
| 16–256 | typical for in-memory pipelines |
| 1k–10k | high-throughput systems where a brief consumer hiccup shouldn't drop items |
| Unbounded | only when the producer is rate-limited *somewhere else* |

## When NOT to use a channel

- **You need broadcast** (one item to many readers). Channels are not pub/sub. Use `IObservable<T>` (Rx), `Action<T>` events, or a custom fan-out.
- **You need priority ordering.** Channels are FIFO. Implement priority by stripping items into multiple channels and a custom reader, or use TPL Dataflow.
- **You need persistence.** Channels are in-memory. For durability, look at message brokers (Kafka, RabbitMQ).

## Common pitfall: `WriteAsync` cancellation

```csharp
await writer.WriteAsync(item, ct);   // throws OCE if ct cancels OR the channel completes
```

Both are normal flow. Catch only the kinds you care about. Don't swallow `ChannelClosedException` if the producer wasn't expected to outlive the channel.

## Run

```bash
dotnet run --project 09-Channels
```
