# Backpressure across Dataflow links

Each block has a `BoundedCapacity`. Producers' `SendAsync` returns a `Task<bool>` that is *not yet completed* if the block is full — the producer naturally awaits.

When linked, slow downstream blocks fill their buffer; once full, upstream's offers to it stop being accepted; the upstream's own buffer fills; the upstream's producers' `SendAsync`s wait. **Backpressure propagates across the chain automatically.**

## What you get for free

```csharp
var fast = new TransformBlock<int, int>(x => x * 2, new() { BoundedCapacity = 16 });
var slow = new ActionBlock<int>(async _ => await Task.Delay(100), new() { BoundedCapacity = 4 });
fast.LinkTo(slow, new DataflowLinkOptions { PropagateCompletion = true });

// the producer paces itself to ~10 items/sec because slow processes at that rate
for (var i = 0; i < 1000; i++) await fast.SendAsync(i);
```

## What you must remember

1. **`BoundedCapacity` is per-block.** Set it on every block, not just the last.
2. **`PropagateCompletion = true`** on every link, otherwise downstream never finishes.
3. **`MaxMessagesPerTask` to fight long-running tasks.** A block delegate can monopolise its task. Set `MaxMessagesPerTask = 100` (or similar) to break long runs and re-pickup, allowing fairer scheduling.

## Per-stage tuning

Real systems rarely have uniform stage costs. Tune per block:

| Stage | Typical knobs |
|---|---|
| Network fetch | `MaxDegreeOfParallelism = 32+`, `BoundedCapacity = 64` |
| CPU transform | `MaxDegreeOfParallelism = ProcessorCount`, `BoundedCapacity = 64` |
| Database write | `MaxDegreeOfParallelism = 1` (transactional) or per-shard count, `BoundedCapacity = 100`, batched via `BatchBlock` |
| Final sink (logging) | `MaxDegreeOfParallelism = 1`, `BoundedCapacity = 100`, drop-old policy if appropriate |

## Detecting the bottleneck

Add metrics for `block.InputCount` per stage. The stage whose input count is consistently full is the bottleneck. Fix by:

- Increasing its `MaxDegreeOfParallelism` (if it's parallelisable).
- Batching upstream so it gets fewer, bigger work units.
- Caching to avoid duplicate work.
- Adding a faster downstream replacement.

## Comparison with manual channels

Manually wiring `Channel<T>` between stages gives you the same backpressure but you have to manage the worker tasks per stage yourself. Dataflow's per-block options consolidate the bookkeeping. The trade-off is per-item overhead.
