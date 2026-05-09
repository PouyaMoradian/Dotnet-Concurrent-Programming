# Over-parallelisation

More parallel ≠ faster. Sometimes it's much slower. Common shapes:

## 1. Parallel inside parallel

```csharp
Parallel.ForEach(outer, item =>
{
    Parallel.ForEach(item.Inner, sub =>
    {
        DoWork(sub);
    });
});
```

Outer fans out to N workers. Each inner fan-out then tries to use N more. The pool gets overwhelmed; context switches dominate. Throughput drops.

Fix: parallelise the *largest* unit, not both. Or flatten and parallelise once.

## 2. Parallel.For over short bodies

```csharp
Parallel.For(0, 1_000_000, i => result[i] = data[i] * 2);
```

Per-iteration overhead (~hundreds of ns) > body cost. The sequential `for` loop is faster.

Fix:

- For tiny bodies, sequential or SIMD (`Vector<T>`).
- Use range partitioner: `Partitioner.Create(0, n, rangeSize: 1024)` so each "iteration" is a chunk.

## 3. Parallel.ForEach over IO-bound work

```csharp
Parallel.ForEach(urls, url =>
{
    var data = http.GetStringAsync(url).Result;   // blocks worker
    Save(data);
});
```

Pool workers pinned waiting for IO. Pool grows; hill-climbing struggles; latency spikes.

Fix: `Parallel.ForEachAsync(urls, options, async (url, ct) => …)`.

## 4. PLINQ over already-PLINQ

```csharp
data.AsParallel()
    .GroupBy(x => x.Key)
    .Select(g => g.AsParallel().Sum(x => x.Value))   // ❌ parallel within parallel
    .ToArray();
```

Outer is already using all cores. The inner `AsParallel()` competes for the same resources.

Fix: drop the inner `AsParallel()`.

## 5. `MaxDegreeOfParallelism = ProcessorCount * N`

People raise it thinking "more must be better." For CPU-bound work, you can't have more useful concurrency than cores. For IO-bound, the right number depends on the *downstream* (rate limit, connection pool size), not your local CPU count.

## When parallelism actually helps

- **Per-iteration work > 10 µs.**
- **No shared mutable state, or the sharing is reduced via `localInit`/`localFinally`.**
- **Source is partitionable** without expensive enumeration.
- **You have idle cores** (not a fully-loaded server already running other workloads).
- **You measured.** Not "feels parallel-ish."

## Diagnosing

- `dotnet-counters` shows full-CPU + slow throughput → CPU is busy doing the wrong thing (context switches, contention).
- BenchmarkDotNet comparing `Parallel.For` vs `for` reveals which wins for your input size.
- PerfView's wall-time stacks show whether workers are "spinning in `Monitor.Enter`" — yes → contention; the parallelism isn't paying off.

## Specific numbers

A rough rule for `Parallel.For` over a CPU-bound body:

| Body cost | Parallel speedup |
|---|---|
| < 100 ns | sequential is faster |
| 100 ns – 1 µs | maybe; benchmark |
| 1 µs – 100 µs | parallel typically wins ~ProcessorCount × |
| > 100 µs | parallel almost always wins |

For IO-bound work, ignore `ProcessorCount`. Concurrency cap = upstream's tolerance.

## The mantra

> **Optimise the algorithm before optimising the parallelism.** A bad algorithm scaled across 32 cores is still a bad algorithm.
