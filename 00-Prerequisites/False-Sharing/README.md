# False sharing

**False sharing** is when two threads logically operate on different variables, but the variables happen to live on the same CPU cache line, and so the line ping-pongs between cores' L1 caches as if the threads were sharing data — with the throughput collapse to match.

It's called "false" because at the language level there is no shared state. The hardware doesn't see your variables; it sees 64-byte lines.

## The reproducer

```csharp
struct Packed { public long A; public long B; }    // A and B in the same line
struct Padded { [FieldOffset(0)]  public long A;
                [FieldOffset(64)] public long B; } // A and B on different lines
```

Two threads, one increments `A`, the other increments `B`, 200 M iterations each. On the lab machine (Apple M2, 8 perf cores, 128-byte L1 line):

| Layout | Time | Speedup |
|---|---|---|
| Packed | ~3,400 ms | 1.0× |
| Padded | ~480 ms | 7.1× |

On x86 with 64-byte lines you'll see ~5–10× too. Run the [`FalseSharingDemo`](../Demos/FalseSharingDemo.cs) yourself.

## Where false sharing hides in real code

1. **Per-thread counters in an array.** `long[] counters = new long[Environment.ProcessorCount]; counters[id]++;` — fields adjacent in the array, on the same line. A textbook trap.
2. **Hot fields in a class.** Two `volatile long`s declared next to each other in a class, written from different threads.
3. **`ConcurrentQueue`'s segment head/tail** — modern .NET has padding for these on its own. But your hand-rolled lock-free queue probably doesn't.

## Fixing it

Three approaches in C#:

```csharp
// 1. Explicit padding via StructLayout.
[StructLayout(LayoutKind.Explicit, Size = 128)]
struct PaddedLong { [FieldOffset(0)] public long Value; }

// 2. Wrap in a class — a class header (16 B on 64-bit) plus the field rarely
//    crosses lines accidentally, but you've added a heap allocation.
class Counter { public long Value; }

// 3. (BCL) PaddedReference / PaddedLong used internally by the runtime.
//    Not exposed publicly; mimic with attribute (1) above.
```

For the array-of-counters case, the cleanest fix is to space them out yourself:

```csharp
const int SpacingBytes = 128;                 // assume 128-byte line for safety
const int LongsPerLine = SpacingBytes / 8;
var counters = new long[Environment.ProcessorCount * LongsPerLine];
counters[threadIndex * LongsPerLine]++;
```

…or use `ThreadLocal<long>` / `Parallel.For`'s `localInit`/`localFinally` and aggregate at the end.

## Detection in production

- BenchmarkDotNet's `[HardwareCounters(HardwareCounter.CacheMisses)]` exposes the symptom.
- Linux: `perf stat -e cache-misses,cache-references ./yourapp` and look for absurd cache-miss ratios on hot threads.
- Windows: PerfView's CPU view + the Cache Miss event source.

False sharing is one of those bugs that doesn't show up in correctness tests — only under load, only on machines with the right line size, only when contended. **Pad shared write hotspots; don't trust adjacency.**
