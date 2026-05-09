# `Parallel.ForEach`

The `IEnumerable<T>` companion to `Parallel.For`. Same chunking, same `localInit`/`localFinally` overloads, same `ParallelLoopState`.

## Source partitioning

The TPL needs to chop the input into chunks. For different sources:

| Source | Partitioner | Notes |
|---|---|---|
| `T[]` / `IList<T>` | range partitioner | best — O(1) random access |
| `IEnumerable<T>` | chunk partitioner | iterates, hands chunks to workers; chunk size grows over time |
| `OrderablePartitioner<T>` | custom | use when you have non-trivial source |

Custom partitioners (`Partitioner.Create(...)`) let you control chunk size — useful for very expensive items where small chunks help work-stealing or very cheap items where larger chunks reduce overhead.

```csharp
// Force a static range partitioner with explicit chunk size
var partitioner = Partitioner.Create(0, items.Length, rangeSize: 1024);
Parallel.ForEach(partitioner, range =>
{
    for (var i = range.Item1; i < range.Item2; i++) DoWork(items[i]);
});
```

The "loop body sees a range" form often outperforms the per-item form for cheap inner work — the framework's per-iteration overhead disappears.

## When ForEach beats For

- Source is naturally `IEnumerable<T>` and you don't want to materialise.
- Source is a partitioner you've tuned.
- The loop body is small and you'd otherwise compute `i` from `partitionStart + offset`.

## Pitfalls (same as `Parallel.For`, plus)

- **Lazy enumerables that block** (a `IEnumerable<T>` whose `MoveNext()` does IO) → workers serialise on the enumerator's lock. Materialise to a list first or use `Parallel.ForEachAsync`.
- **`ToList()` of a huge source before parallelism** — defeats streaming. Choose: stream with chunk partitioner, or materialise once.
- **Side effects on the source** during enumeration → undefined behaviour. The source must be read-only during the loop.

## Anti-pattern: `Parallel.ForEach` over async work

```csharp
// ❌
Parallel.ForEach(urls, url =>
{
    var data = httpClient.GetStringAsync(url).Result;   // sync-over-async, pins worker
});
```

Use `Parallel.ForEachAsync` — it's the entire reason it exists.

## Real-world checklist

- Is each iteration > ~10 µs? → likely worth parallelising.
- Is each iteration < 1 µs? → measure first; partition overhead may dominate.
- Does the body call shared state? → use `localInit`/`localFinally` to avoid per-iteration contention.
- Are exceptions tolerable? → `Parallel.ForEach` aggregates partition-side exceptions.
