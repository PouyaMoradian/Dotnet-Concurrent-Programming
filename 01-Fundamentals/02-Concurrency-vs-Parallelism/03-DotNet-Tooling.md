# .NET tooling — which API gives you which property?

A quick reference of every concurrency / parallelism API in .NET and where it sits on the structure × execution grid. Keep this open the first few times you reach for one.

## The grid

| API | Concurrency? | Parallelism? | Best for |
|---|---|---|---|
| `Task` (plain) | Maybe | Maybe | Promise type — neither, alone |
| `async`/`await` | **Yes** | Incidental on ASP.NET Core; no on UI | IO-bound, latency hiding |
| `Task.Run` | Yes | **Yes** (if cores free) | Offload CPU work from a UI thread |
| `Task.Factory.StartNew(..., LongRunning)` | Yes | Yes | Long-running dedicated work that shouldn't perturb the pool |
| `Task.WhenAll` | Yes | Yes if the underlying tasks hit different threads | Fan-out / fan-in |
| `Task.WhenAny` | Yes | Same | First-result / timeout |
| `Parallel.For` / `Parallel.ForEach` | Yes | **Yes** | Data-parallel, CPU-bound, large per-iteration cost |
| `Parallel.Invoke` | Yes | **Yes** | Static fork-join with a fixed number of branches |
| `Parallel.ForEachAsync` | **Yes** | Yes, with a concurrency cap | Bounded concurrent async over a collection |
| PLINQ (`AsParallel()`) | Yes | **Yes** | Data-parallel queries with associative reductions |
| `Channel<T>` | **Yes** | Yes if multiple consumers | Producer/consumer, back-pressure |
| `BlockingCollection<T>` | Yes | Yes | Same, but blocking — prefer `Channel<T>` in new code |
| TPL Dataflow blocks | Yes | Yes | Pipelines with backpressure and bounded queues |
| `SemaphoreSlim` (with `WaitAsync`) | Yes | — | Concurrency limit ("at most N in flight") |
| `ConcurrentDictionary<K,V>` | Yes (read) | Yes | Many-reader / few-writer maps |
| `Vector<T>` / `Vector256<T>` | No | **Yes** (within one thread) | Tight numeric loops |
| `Thread` | Yes | Yes | The escape hatch — see 01-Processes-vs-Threads |

## The "obvious" matches

If you're picking a tool from a single classification of the work, this is the short answer:

| The work is… | Use |
|---|---|
| Many independent IO calls | `Task.WhenAll` over `await`-style methods |
| A bounded number of concurrent IO calls | `Parallel.ForEachAsync` with `MaxDegreeOfParallelism` |
| A CPU-bound loop over a collection | `Parallel.For` / `Parallel.ForEach` |
| A CPU-bound LINQ chain | `AsParallel()` |
| A tight numeric loop, single-threaded | `Vector<T>` / SIMD intrinsics |
| Producer/consumer with backpressure | `Channel<T>` (bounded) |
| Multiple stages, each transforming items | TPL Dataflow |
| One actor owning state | A single reader on a `Channel<T>` |

## The four common mismatches

These are the classifications people get wrong most often.

### 1. `Parallel.ForEach` over async IO

```csharp
// Wrong
Parallel.ForEach(urls, url =>
{
    var data = httpClient.GetStringAsync(url).GetAwaiter().GetResult();
    Save(data);
});
```

The `GetAwaiter().GetResult()` blocks a pool thread per item. `Parallel.ForEach`'s parallelism cap defaults to `ProcessorCount` — so you can issue at most ~16 concurrent HTTP calls instead of hundreds.

```csharp
// Right
await Parallel.ForEachAsync(urls,
    new ParallelOptions { MaxDegreeOfParallelism = 32 },
    async (url, ct) =>
    {
        var data = await httpClient.GetStringAsync(url, ct);
        await SaveAsync(data, ct);
    });
```

### 2. `async`/`await` around CPU-bound work

```csharp
// Pointless
public async Task<double> Compute(int n)
{
    return await Task.Run(() => HeavyMath(n));
}
```

If the caller is on the ThreadPool already, you've added a queue hop for no benefit. Just call `HeavyMath(n)` synchronously. The `Task.Run` is only useful when the caller is a UI thread and you specifically want to *get off* it.

### 3. `Task.WhenAll` over already-sequential work

```csharp
// Misleading
await Task.WhenAll(items.Select(async i => await ProcessAsync(i)));
// ↑ this DOES run them concurrently, but...

// If ProcessAsync is purely synchronous internally:
await Task.WhenAll(items.Select(i => ProcessAsync(i)));
// ↑ ...each call returns a completed task with no actual yield, and
//   you get a sequential loop with extra allocations.
```

### 4. `Task.Run` for genuinely async code

```csharp
// Wrong
public Task<string> GetAsync(string url) =>
    Task.Run(() => httpClient.GetStringAsync(url));
// You've taken async IO and dropped it onto a pool thread.
// The HTTP call itself is non-blocking; pool involvement adds latency, not parallelism.

// Right
public Task<string> GetAsync(string url) =>
    httpClient.GetStringAsync(url);
// Or, with an awaiter:
public async Task<string> GetAsync(string url) =>
    await httpClient.GetStringAsync(url);
```

## Building a habit

When you reach for any of these APIs, ask the two questions in order:

1. *Is the work IO-bound or CPU-bound?* If IO, you want concurrency (`async`, `Channel`, `Parallel.ForEachAsync`). If CPU, you want parallelism (`Parallel.*`, PLINQ, SIMD).
2. *Is there shared state?* If yes, pick a sync primitive (chapter 04), an atomic (chapter 05), or — best — refactor to remove the sharing.

Get those two right and 90% of API selection is automatic.
