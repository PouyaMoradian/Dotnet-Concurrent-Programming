# Backpressure

**Backpressure** = a slow consumer slows the producer. Without it, a system has no built-in protection against overload — it just absorbs incoming load until something snaps (memory, latency, request timeouts).

A bounded channel with `FullMode = Wait` *is* backpressure. The producer's `await ch.Writer.WriteAsync(item)` returns only when there's room. The producer naturally paces.

## Why every system needs it somewhere

Imagine a request pipeline: `HTTP → parse → enrich → write to DB → respond`.

Without backpressure, if the DB slows:

- Enrich keeps producing.
- Parse keeps producing.
- The HTTP handler keeps accepting.
- The "to-write" queue grows unboundedly → memory grows → eventually crash.

With backpressure:

- The DB-bound channel fills.
- Enrich blocks on its next write.
- Parse blocks on its next write.
- The HTTP handler eventually blocks on intake — the load shedding has propagated to the boundary, where it's *visible* (returning 503/429).

Backpressure pushes the failure to the boundary where you can take action.

## Channels' three forms

### 1. Strict (`FullMode = Wait`)

```csharp
await ch.Writer.WriteAsync(item, ct);   // producer blocks; classic backpressure
```

### 2. Drop-old (`FullMode = DropOldest`)

```csharp
ch.Writer.TryWrite(item);   // never blocks; old items evicted to make room
```

For *latest snapshot* semantics: telemetry, status updates, "current state" feeds.

### 3. Drop-new (`FullMode = DropNewest`)

Rare. The newest item is rejected when full. Used when freshness doesn't matter and you'd rather keep the queue you've built.

## Backpressure isn't only about channels

Other forms in .NET:

- **`SemaphoreSlim`-bounded fan-out** — `Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = N })` paces because at most N tasks run at once.
- **`RateLimiter`** (.NET 8) — pace by *time*, not just by *concurrency*. See [16/RateLimiting](../../16-Modern-.NET-Features/RateLimiting).
- **TPL Dataflow `BoundedCapacity`** — the same idea in TPL Dataflow ([Chapter 10](../../10-TPL-Dataflow)).

## Failure modes

When backpressure kicks in, the consumer must **react meaningfully**. Common right answers:

- Return a 429 / 503 to the upstream client.
- Pause an ingestion thread (`Kafka` consumer slows reading from the broker).
- Spill to disk (a write-ahead log) so memory stays bounded.

Wrong answers:

- Silently drop without logging.
- "Just retry" — the underlying problem doesn't go away.

## Checklist for any pipeline you build

1. ✅ Every producer-consumer hop is bounded.
2. ✅ Bounded capacity is sized based on **expected steady-state**, not peaks.
3. ✅ `FullMode` is chosen consciously per stage (most stages: `Wait`).
4. ✅ Consumer side has a metric for queue length and a metric for `WriteAsync` wait time.
5. ✅ The boundary (HTTP handler / Kafka consumer / etc.) reacts when backpressure propagates to it.

If you can't tick all five, you don't have backpressure — you have an OOM in waiting.
