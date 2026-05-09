# `Parallel.For`

Declarative parallel loop over an integer range. Trades a tiny amount of overhead per iteration for the ability to use all cores without manual partitioning.

## Basics

```csharp
Parallel.For(0, n, i => DoWork(i));
```

The TPL chooses a partitioner that divides the range into chunks per worker. The default chunking uses dynamic work-stealing — workers grab small chunks at a time from a shared queue, refilling fast workers more often than slow ones.

## The `localInit` / `localFinally` overload — *learn this one*

For reductions, the per-iteration shared accumulator is a contention disaster. Use the per-partition local form:

```csharp
long total = 0;
Parallel.For(0, n,
    localInit:  () => 0L,
    body:       (i, state, local) => local + data[i],
    localFinally: local => Interlocked.Add(ref total, local));
```

`body` returns the new local accumulator (yes, returns — the local is *immutable* across iterations from the framework's perspective). `localFinally` runs once per partition; that's where you commit to the shared total. Result: contention on `total` happens N times, where N is partition count, not iteration count.

## `ParallelLoopState`

The `state` parameter lets you stop the loop early:

```csharp
Parallel.For(0, n, (i, state) =>
{
    if (FoundIt(i)) state.Stop();
    if (BadCondition()) state.Break();
});
```

- `Stop()` — request termination ASAP. Other partitions may finish what they were doing but don't start new iterations.
- `Break()` — process iterations strictly *before* this one but skip later ones (semantics for ordered loops).
- `IsExceptional` / `IsStopped` / `LowestBreakIteration` for inspection.

## ParallelOptions

```csharp
Parallel.For(0, n, new ParallelOptions
{
    MaxDegreeOfParallelism = Environment.ProcessorCount / 2,
    CancellationToken = ct,
}, body);
```

`MaxDegreeOfParallelism = 1` is the way to "run synchronously but with ParallelOptions plumbing" for testing.

## When NOT to use `Parallel.For`

- **IO-bound work** — pins workers; use `Parallel.ForEachAsync`.
- **Tiny iterations** — overhead dominates. If each iteration is < 100 ns of work, batch them or stay sequential. The autovectoriser is also better at sequential tight loops.
- **Already-parallel inner work** — wrapping `Parallel.For` around another parallel construct creates fan-out explosions.

## Performance tip: cache-friendly access

A `Parallel.For(0, N, ...)` partitioner divides into ranges. If two partitions write to adjacent memory, you risk false sharing. For per-iteration writes to `output[i]`, this is usually OK (each iteration writes its *own* slot). For per-partition writes (e.g., `output[partitionId]`), pad the output array.
