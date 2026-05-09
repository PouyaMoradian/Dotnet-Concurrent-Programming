# Bounded channels

`Channel.CreateBounded<T>(capacity)` gives you a fixed-size buffer. Producers wait when full; consumers wait when empty. **This is the default you should reach for.**

## Configuration

```csharp
var ch = Channel.CreateBounded<T>(new BoundedChannelOptions(1024)
{
    SingleReader = true,                                  // optimisation hint
    SingleWriter = true,                                  // optimisation hint
    FullMode     = BoundedChannelFullMode.Wait,           // backpressure on producer
    AllowSynchronousContinuations = false,                // safe default
});
```

### `FullMode`

| Mode | Behaviour |
|---|---|
| `Wait` | Producer awaits a free slot — strict backpressure |
| `DropOldest` | Drop the head item to make room; favoured for "latest snapshot" telemetry |
| `DropNewest` | Drop the just-arriving item; rare; favoured when stale-data is preferred |
| `DropWrite` | The write completes-faulted-with-`false` if full; producer must handle |

For business workflows: `Wait`. For metrics/telemetry where stale is worse than missing: `DropOldest`.

### `SingleReader` / `SingleWriter`

Hints to the implementation that there's only one of each. The channel uses a faster code path with fewer locks. If you violate the hint at runtime, behaviour is *undefined*. **Set these correctly or leave them at `false`.**

### `AllowSynchronousContinuations`

If `true`, completion of the write/read can run continuations *inline on the calling thread*. Faster, but a continuation that does any non-trivial work blocks the caller. **Default `false` is the safe choice** unless you've measured a benefit and proved no continuation does any work.

## Sizing

| Capacity | Use |
|---|---|
| 1 | Rendezvous: producer and consumer hand off in lockstep |
| 16–256 | Typical microservice in-memory pipeline |
| 1024–10000 | High-throughput; consumer has occasional GC pauses you'd absorb |
| > 10K | Suspicious — almost certainly an unbounded leak in disguise |

The channel allocates one segment of `capacity * sizeof(T)` references upfront. A `Channel<long>(8192)` is 64 KB, gone before the first item.

## Pitfalls

1. **Forgetting `Writer.Complete()`** — readers `await foreach` forever.
2. **Mixing `WriteAsync` and `TryWrite`** — `TryWrite` returns `false` when full (*if* `FullMode = Wait`); easy to spin on it.
3. **Not propagating cancellation** — pass `ct` to `WriteAsync` / `ReadAsync` so a stuck producer/consumer cancels cleanly.

## Pattern: bounded channel as concurrency cap

A `Channel<Unit>` with capacity N is a perfectly serviceable async semaphore:

```csharp
var slots = Channel.CreateBounded<Unit>(N);
for (var i = 0; i < N; i++) slots.Writer.TryWrite(default);

async Task<T> RunWithCap(Func<Task<T>> work)
{
    await slots.Reader.ReadAsync();          // acquire
    try { return await work(); }
    finally { await slots.Writer.WriteAsync(default); }
}
```

For more than ~hundreds of concurrent waiters, prefer `SemaphoreSlim` (more efficient queueing). For < 100, the channel version is fine and clearer.
