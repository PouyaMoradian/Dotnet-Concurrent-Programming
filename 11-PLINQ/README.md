# 11 — PLINQ (Parallel LINQ)

> **Layer:** BCL
> **Reading time:** ~25 minutes
> **Prereq:** [07](../07-Task-Parallel-Library/)

PLINQ is data-parallel LINQ. Take an `IEnumerable<T>`, call `.AsParallel()`, and the rest of the LINQ chain runs across multiple cores.

It's narrowly applicable. Use it when:

- The source is an in-memory collection (or partitionable enumerable).
- The per-element work is non-trivial (not just `i * 2`).
- The pipeline has no order dependency (or you accept ordering cost).
- You want the *declarative* shape of LINQ rather than imperative `Parallel.For`.

If you're outside those conditions, either `Parallel.For` / `Parallel.ForEach` (imperative) or async pipelines (`Channel<T>` / Dataflow) are better.

## In-chapter folders

| Folder | Topic |
|---|---|
| [Partitioning](Partitioning/) | How PLINQ chops the source — range / chunk / hash partitioners |
| [MergeStrategies](MergeStrategies/) | How outputs reassemble — `NotBuffered` / `AutoBuffered` / `FullyBuffered` |
| [Ordering](Ordering/) | `AsOrdered` and what it costs |
| [Aggregation](Aggregation/) | `Aggregate` with seed/local/combine for parallel reductions |
| [PerformanceTuning](PerformanceTuning/) | When PLINQ wins, when it loses, the knobs |

## A canonical example

```csharp
var results = Enumerable.Range(0, 1_000_000)
    .AsParallel()
    .WithDegreeOfParallelism(Environment.ProcessorCount)
    .WithMergeOptions(ParallelMergeOptions.NotBuffered)
    .Where(IsPrime)
    .Select(p => p * p)
    .ToArray();
```

Each operator runs across all cores. Output ordering matches input by default (`AsOrdered` is implicit on `Range`); reorder loosely with `.AsUnordered()` for ~10–20% throughput.

## When PLINQ is the wrong tool

- **Per-element work < ~1 µs.** Partition overhead dominates.
- **Order matters strictly.** `AsOrdered` works but costs throughput.
- **Async/IO inside the operators.** PLINQ is sync; use `Parallel.ForEachAsync`.
- **Unbounded sources.** PLINQ wants to know enough to partition; an infinite stream from a network channel is wrong.

## Run

```bash
dotnet run --project 11-PLINQ
```
