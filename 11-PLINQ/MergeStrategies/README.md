# Merge strategies

After workers produce output, PLINQ has to merge the streams back into one for the consumer. Three strategies:

| `ParallelMergeOptions` | Behaviour |
|---|---|
| `NotBuffered` | Stream items as fast as they arrive — lowest latency to first item |
| `AutoBuffered` (default) | Buffer per-worker chunks; merge in chunks |
| `FullyBuffered` | Buffer the entire result; emit at end |

Set with `.WithMergeOptions(ParallelMergeOptions.NotBuffered)`.

## Choosing

- **`NotBuffered`** when: the consumer (e.g., `foreach`) is interactive and you want first results ASAP.
- **`AutoBuffered`** when: you want throughput, don't care about first-item latency.
- **`FullyBuffered`** when: the next stage expects a complete collection and is happy to wait. Implicit on `ToArray()`/`ToList()`.

## When ordering interacts

If you've used `.AsOrdered()`, the merger needs to preserve input order even though workers complete in arbitrary order. This forces buffering: items finishing out-of-order are held until predecessors arrive. `NotBuffered + AsOrdered` is mostly contradictory — the merger must buffer to maintain order, despite the option.

## What you'll see

- `AsOrdered + NotBuffered` → merger buffers anyway; throughput similar to `AutoBuffered`.
- `AsUnordered + NotBuffered` → fastest streaming.
- `AsOrdered + FullyBuffered` → highest peak memory; entire result materialised in order.

## In practice

Most PLINQ chains end in `.ToArray()` or `.Sum()`/`.Count()`, where the merger is implicit and buffering matters less. The merge options are for streaming consumption — relatively rare.
