# False sharing

Already covered in detail in [00/False-Sharing](../../00-Prerequisites/False-Sharing/). This is the "in production" view.

## The signature

You wrote a parallel algorithm. Each thread updates its *own* counter (or its own slot in an array). You expect linear scaling with cores. You measure: throughput barely changes — sometimes *worse* with more cores. CPU is pinned but cache misses are absurd.

That's false sharing.

## Where it shows up

1. **Per-thread counters in an array** with default layout.
2. **Hot fields in a class** declared adjacent.
3. **`Lock` / `Mutex` / `Monitor`** internal state when many of them live in adjacent memory.
4. **Hand-rolled queues** without padding.

## The fix in production code

### Per-thread counter array

```csharp
// ❌
var counters = new long[Environment.ProcessorCount];
counters[id]++;            // adjacent longs share a cache line

// ✅
const int Padding = 16;     // 16 longs = 128 bytes — fits two cache lines on Apple M
var counters = new long[Environment.ProcessorCount * Padding];
counters[id * Padding]++;
```

### Hot fields in a class

```csharp
[StructLayout(LayoutKind.Explicit, Size = 256)]
public struct Hot
{
    [FieldOffset(0)]   public long ProducerCounter;
    [FieldOffset(128)] public long ConsumerCounter;
}
```

Or wrap each in a class:

```csharp
sealed class Counter { public long Value; }
var perThread = Enumerable.Range(0, n).Select(_ => new Counter()).ToArray();
```

The class header gives you separation but adds an indirection — measure.

### Use `Parallel.For`'s `localInit` / `localFinally`

The framework gives each partition its own `localState`. Aggregate at the end. No false sharing because the locals live on different stacks.

```csharp
long total = 0;
Parallel.For(0, n,
    () => 0L,
    (i, _, local) => local + work(i),
    local => Interlocked.Add(ref total, local));
```

## Detection

- **BenchmarkDotNet** with `[HardwareCounters(HardwareCounter.CacheMisses, HardwareCounter.CacheReferences)]`. Watch for absurd cache-miss ratios.
- **Linux `perf stat`**: `perf stat -e cache-misses,cache-references ./MyApp`. If the ratio is > 50%, suspect false sharing.
- **Counter scaling test**: run with 1, 2, 4, 8 threads; if throughput doesn't grow proportionally, it's contention — locks, true sharing, or false sharing.

## A subtle case: BCL types

Some BCL types (`ConcurrentQueue<T>`'s segments, `ThreadPool` internals) are already padded against false sharing. Your *external* references to them aren't necessarily aligned, but the internal hot fields are. So you don't usually need to pad around BCL types.

## On Apple Silicon

M1/M2/M3 use **128-byte cache lines** (vs x86's 64). Padding to 64 is enough on x86; 128 is safer cross-platform. The chapter's `FalseSharingDemo` uses 128 to be portable.

## When NOT to pad

- Read-only data: false sharing only affects *writes*.
- Cold data: if each thread accesses its slot rarely, the cost is negligible.
- Generic libraries: padding everything wastes memory; pad targeted hotspots.

## Anecdote

A team I worked with had a `lock` per cache entry (millions of cache entries). Each `lock` (well, the boxed object) was ~24 bytes on 64-bit; many fit per cache line. Concurrent writes to *different* keys produced cache contention because their lock objects shared lines. Refactoring to a striped `lock` (small fixed array of locks per cache, padded) was a 4× throughput improvement.
