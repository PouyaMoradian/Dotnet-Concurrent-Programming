# Cache coherency — overview

Multiple cores, each with their own L1 and L2, **must** see a consistent view of memory — that is the single hardware promise that makes `Interlocked.CompareExchange` meaningful. The mechanism that delivers it is the **cache coherence protocol** running, invisibly, between the cores' L1s and the shared L3.

This section walks the memory hierarchy from registers to DRAM, explains what the protocol does and what it costs, and translates the mechanism into concrete .NET patterns that scale (and ones that don't).

## What's in this section

| File | What it covers |
|---|---|
| [01-Memory-Hierarchy.md](01-Memory-Hierarchy.md) | The full latency ladder from register to remote DRAM, cache-line size, prefetchers, working-set rules of thumb |
| [02-MESI-and-Variants.md](02-MESI-and-Variants.md) | The four **MESI** states (Modified / Exclusive / Shared / Invalid); **MESIF** (Intel — adds a Forwarder state) and **MOESI** (AMD — adds an Owned state); state diagrams; what events trigger transitions |
| [03-RFO-and-Contention.md](03-RFO-and-Contention.md) | The expensive S→M transition, the **RFO** (Read For Ownership) message, why writes are the contention point, and the scaling cliff |
| [04-DotNet-Patterns.md](04-DotNet-Patterns.md) | The patterns that work *with* coherence (per-thread state, sharding, immutable reads) and the ones that fight it |

## The 60-second summary

```
                ┌─────────────┐
   core 0:      │ L1d (32 KB) │ ← 4 cycles
                └─────┬───────┘
                      │
                ┌─────▼───────┐
                │ L2 (1 MB)   │ ← 12 cycles
                └─────┬───────┘
                      │
              ────────┼────────                  ┌──────────────────┐
                      ▼                          │  cache-coherence │
                 ┌────────┐                      │  fabric (ring,   │
                 │ L3 LLC │ ← 40 cycles, shared │  mesh, or fabric)│   (LLC = Last-Level Cache)
                 └────┬───┘                      └──────────────────┘
                      │
                  ┌───▼───┐
                  │ DRAM  │ ← ~80–100 ns local; 140–200 ns remote
                  └───────┘
```

The three rules everything else follows from:

1. **One writer is cheap.** The line stays in the writer's L1 in Modified state.
2. **Many readers are cheap.** Every reader keeps the line in Shared state in its own L1.
3. **Many writers to the same line are expensive.** The line ping-pongs through the coherence fabric on every write — that's the cost of MESI's correctness.

## The smallest example that matters

```csharp
// SLOW: every increment is one RFO across the chip
private static long _counter;
Parallel.For(0, 1_000_000, _ => Interlocked.Increment(ref _counter));

// FAST: each core has its own counter, summed at the end
var locals = new long[Environment.ProcessorCount * 16];   // crude line-padding
Parallel.For(0, 1_000_000,
    () => 0L,
    (i, _, local) => local + 1,
    local => Interlocked.Add(ref locals[Thread.GetCurrentProcessorId() * 16], local));
long total = locals.Sum();
```

The first version is throttled by the speed of the coherence fabric — typically ~20–50 ns per increment under contention. The second runs at the speed of L1 — typically <1 ns. The work the program *does* is identical; the difference is whether the cores are fighting over a cache line.

## How this maps to the rest of the chapter

- **`Interlocked` is correct but contended.** Use it for state transitions, not for hot counters.
- **False sharing** ([../False-Sharing](../False-Sharing/)) is the version of this where the threads *don't realise* they're writing to the same line.
- **NUMA** ([../NUMA](../NUMA/)) is the version where "the L3" is actually socket-local and remote accesses pay the interconnect tax.
- **Memory model** ([../CPU-Architecture/04-Store-Buffer-and-Memory-Ordering.md](../CPU-Architecture/04-Store-Buffer-and-Memory-Ordering.md)) is the contract built *on top* of cache coherence — coherence guarantees the line is consistent; the memory model says *when* that consistency becomes observable.

## Demos in this chapter that exercise this section

- **`MemoryLatencyLadderDemo`** (demo 6) — walks arrays of growing sizes; the latency steps mark the L1/L2/L3/DRAM boundaries.
- **`ContendedInterlockedDemo`** (demo 7) — one shared counter vs N sharded counters.
- **`FalseSharingDemo`** (demo 1) — the most expensive single line of bad code in a concurrent program.

## Further reading

- **Paul McKenney — *Memory Barriers: a Hardware View for Software Hackers*** (free PDF; canonical).
- **Intel SDM Vol. 3A** — "Memory Ordering" and "Memory Management" chapters.
- **Jeff Preshing — `preshing.com`** — the most accessible MESI walkthrough on the internet.
- **Ulrich Drepper — *What Every Programmer Should Know About Memory*** (2007) — older, but the architecture chapter still applies.
