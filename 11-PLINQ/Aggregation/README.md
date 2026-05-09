# Aggregation

The simple `.Sum()` / `.Count()` / `.Min()` operators have parallel implementations under the hood. For custom reductions, use `Aggregate` — and use the **four-argument overload** for performance.

## The wrong overload

```csharp
data.AsParallel().Aggregate(0L, (acc, x) => acc + x);
```

This is a serial-style aggregate dressed up. Each step requires `acc` to be the same shared accumulator → contention.

## The right overload

```csharp
long sum = data.AsParallel().Aggregate(
    seedFactory:                   () => 0L,            // per-partition seed
    updateAccumulatorFunc:         (acc, x) => acc + x, // body, *thread-local* acc
    combineAccumulatorsFunc:       (a, b) => a + b,     // combine partition results
    resultSelector:                x => x);
```

Each partition has its **own accumulator**. The body runs without contention. Only at the end are partition accumulators combined. This is the parallel reduction shape — `Parallel.For`'s `localInit`/`localFinally` is the exact same idea.

## When you need the four-arg form

Whenever the per-element work is non-trivial *and* the accumulator is mutated per element. For something like:

```csharp
data.AsParallel().Aggregate(
    () => new HashSet<int>(),
    (set, x) => { set.Add(x); return set; },
    (a, b) => { a.UnionWith(b); return a; },
    set => set.Count);
```

This builds per-partition `HashSet<int>`s in parallel and unions them at the end. The single-acc version would have all partitions racing on one set.

## Trap: mutating the accumulator without returning

```csharp
.Aggregate(() => new List<int>(),
           (list, x) => { list.Add(x); return list; },   // ← must return list
           ...);
```

The accumulator type must be returned even if you're mutating it (because the framework treats it as a value).

## Comparison with `Parallel.For` reduction

`Parallel.For` with `localInit`/`localFinally` is an imperative way to express the same thing. PLINQ's `Aggregate` is declarative. Pick based on whether the rest of the pipeline is LINQ-shaped or `for`-shaped.
