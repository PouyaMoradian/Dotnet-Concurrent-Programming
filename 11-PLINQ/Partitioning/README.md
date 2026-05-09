# Partitioning

PLINQ first slices the source into chunks across worker threads. The right partitioner depends on the source.

| Source | Partitioner |
|---|---|
| `T[]` / `IList<T>` | range partitioner — O(1) split |
| `IEnumerable<T>` | chunk partitioner — locked enumerator handing chunks |
| Custom `Partitioner<T>` | yours |

## Range partitioner

For an array of N items and P workers, slice into P contiguous ranges. Hot in cache; no per-item synchronisation.

## Chunk partitioner

Used when the source is an opaque `IEnumerable<T>`. Workers contend for `MoveNext` on a single shared enumerator (under a lock); each worker pulls a *chunk* (initially 1 item; grows over time). The growth schedule reduces lock contention without giving any one worker too much.

The chunk approach is general but slower than range partitioning. If you have a large array, prefer keeping it as `T[]` rather than passing it as `IEnumerable<T>` — preserves the range partitioner.

## Custom partitioners

`Partitioner.Create(0, count, rangeSize)` builds a static range partitioner with explicit chunk size:

```csharp
var partitioner = Partitioner.Create(0, items.Length, rangeSize: 1024);
items.AsParallel().Where(...).Sum(...);
// or for Parallel.ForEach:
Parallel.ForEach(partitioner, range =>
{
    for (var i = range.Item1; i < range.Item2; i++) DoWork(items[i]);
});
```

Useful when:

- Items are very cheap → bigger chunks reduce overhead.
- Items vary wildly in cost → smaller chunks help work-stealing.

## Hash partitioner (for `GroupBy`/`Join`)

PLINQ's `GroupBy` partitions by hash so that all elements with the same key end up in the same partition. The hash partitioner has a barrier-like phase: each partition must finish hashing its share before the next operator begins.

## What you should remember

- **Pass arrays/lists, not LINQ-of-LINQ.** Arrays partition O(1); chained `IEnumerable<T>` partitions with locks.
- **Tune chunk size only with measurement.** Default is usually fine.
- **Range partitioners + range-aware bodies** are the fastest; for `Parallel.For`/`Parallel.ForEach`, use the range overload (`Partitioner.Create(0, n, rangeSize)`).
