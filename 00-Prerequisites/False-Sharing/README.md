# False sharing — overview

**False sharing** is when two threads logically operate on different variables, but the variables happen to live on the same CPU cache line, and so the line ping-pongs between cores' L1 caches as if the threads were sharing data — with the throughput collapse to match.

It's called "false" because at the language level there is no shared state. The hardware doesn't see your variables; it sees 64-byte lines. From the cache-coherence protocol's perspective, two threads writing to *adjacent fields* are indistinguishable from two threads writing to *the same field*. Both cause **RFO** (Read For Ownership) traffic on every store.

This section is the practical anatomy of false sharing: how the mechanism turns into the bug, where it hides in real code, how to fix it, and how to detect it.

## What's in this section

| File | What it covers |
|---|---|
| [01-Mechanism.md](01-Mechanism.md) | Cache lines, the **MESI** (Modified / Exclusive / Shared / Invalid) coherence protocol, what specifically happens in silicon when adjacent fields are hot |
| [02-Where-It-Hides.md](02-Where-It-Hides.md) | Real-world cases — per-thread counter arrays, hot class fields, lock-free queue heads/tails, struct layouts |
| [03-Fixing-It.md](03-Fixing-It.md) | Padding via `StructLayout`, `ThreadLocal`, sharding patterns, padded types inside the **BCL** (Base Class Library) |
| [04-Detecting-It.md](04-Detecting-It.md) | Hardware counters, BenchmarkDotNet diagnostics, `perf c2c`, Visual Studio profilers |

## The 60-second summary

```
   Two threads, two unrelated longs:

   struct Bad {                    Memory layout:
       public long A;              ┌────────────────────────────┐
       public long B;              │  A  │  B  │ ... rest ...   │
   }                               └────────────────────────────┘
                                   ◄────── 64 B cache line ─────►

   thread 1: writes A  →  invalidates entire line in thread 2's cache
   thread 2: writes B  →  invalidates entire line in thread 1's cache
   thread 1: writes A  →  fetches line back (RFO), then writes
   ... and so on, forever
```

The cost: each increment takes ~50–100 ns instead of ~1 ns. On a tight loop running 100M iterations on 2 threads, that's 10 seconds vs 0.1 seconds — a 100× slowdown for the same logical work.

## Where to look first

When a piece of multithreaded code "should" be faster than it is, false sharing is on the short list of suspects. The smell tests:

1. **Per-thread counters in an array** — `long[] counters = new long[ProcessorCount]; counters[id]++;` — 8-byte longs pack 8 per 64 B line.
2. **Adjacent `long`/`int` fields in a class touched by different threads.**
3. **Head/tail pointers in a hand-rolled queue.** A producer increments tail; a consumer reads/increments head; both can sit on one line.
4. **Statistics objects with side-by-side counters** — request count and error count on the same class, written from different threads.

When in doubt, *pad*. Padding to 128 bytes covers x86 (64 B line) and Apple Silicon (128 B line). Cost: 120 wasted bytes per counter — irrelevant for code-path correctness, meaningful only if you have millions of these.

## The fix in one line

```csharp
[StructLayout(LayoutKind.Explicit, Size = 128)]   // own cache line(s)
struct PaddedLong { [FieldOffset(0)] public long Value; }
```

Replace `long X` with `PaddedLong X` in any hot, contended field. Throughput typically restores to the no-coherence-cost level.

## Demos in this chapter that exercise this section

- **`FalseSharingDemo`** (demo 1) — the canonical packed-vs-padded benchmark.
- **`CacheLineProbe`** (demo 0) — measures your effective cache-line size; you need this to know how much to pad.
- **`ContendedInterlockedDemo`** (demo 7) — sharded counter vs single counter; the sharded version has its own padding (otherwise the shards false-share!).

## How this relates to the rest of the chapter

- **`Cache-Coherency`** explains the protocol. False sharing is the *unintended* version of the contention that protocol causes.
- **`CPU-Architecture`** explains the pipeline. False sharing stalls the store buffer.
- **`NUMA`** raises the stakes — on a multi-node box, false-sharing traffic crosses sockets.

## Further reading

- **Herb Sutter — *Eliminate False Sharing*** — Dr. Dobb's article, the canonical introduction.
- **Martin Thompson — `mechanical-sympathy.blogspot.com`** — many practical posts on padding and lock-free queues.
- **Wikipedia — *False sharing*** — has worked examples and animations.
