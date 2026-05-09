# Cheat sheets

## Concurrency primitive selection

```
Need to coordinate threads on this object?
├── Async path? → SemaphoreSlim.WaitAsync(1,1) [or Nito.AsyncEx.AsyncLock]
└── Sync only?
    ├── Single process?
    │   ├── Many readers, rare writer (and you measured)? → ReaderWriterLockSlim
    │   ├── Critical section is a single primitive op? → Interlocked
    │   └── Otherwise → lock (System.Threading.Lock on .NET 9+)
    └── Cross-process? → Mutex
```

## Async pattern selection

```
                                                What are you doing?
                ┌──────────────────────────────────────┴─────────────────────────────────┐
                IO                                                                       CPU
                │                                                                        │
        ┌───────┴────────┐                                                       ┌───────┴────────┐
        Single op       Many ops                                                Single loop      Many loops over a set
            │              │                                                        │                  │
            await        Parallel.ForEachAsync(...,                            sequential          Parallel.For/ForEach
                          MaxDegreeOfParallelism = N)                               │                  │
                                                                                with SIMD?           localInit/localFinally
                                                                                Vector<T>            for reductions
```

## Pipeline-shape selection

```
Need a typed FIFO between stages?
├── Same types throughout, simple? → Channel<T>
├── Per-stage parallelism, predicate routing? → TPL Dataflow
├── Push-based event streams? → Rx (System.Reactive)
└── Network bytes, parsing? → System.IO.Pipelines
```

## Common code shapes

### Cancellable async loop

```csharp
while (!ct.IsCancellationRequested)
{
    await DoOneIterationAsync(ct);
}
```

### Bounded fan-out

```csharp
await Parallel.ForEachAsync(items,
    new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
    async (item, innerCt) => await ProcessAsync(item, innerCt));
```

### Lazy-cached factory

```csharp
private readonly ConcurrentDictionary<K, Lazy<V>> _cache = new();
public V Get(K key) =>
    _cache.GetOrAdd(key, k => new Lazy<V>(() => Build(k),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;
```

### Async mutex

```csharp
private readonly SemaphoreSlim _gate = new(1, 1);
async Task DoOneAtATimeAsync(CancellationToken ct)
{
    await _gate.WaitAsync(ct);
    try { await DoAsync(ct); }
    finally { _gate.Release(); }
}
```

### Atomic publish

```csharp
private static Configuration _current = new();
public static Configuration Current => Volatile.Read(ref _current);
public static void Publish(Configuration v) => Volatile.Write(ref _current, v);
```

### Lock-free counter (sharded)

```csharp
const int Spacing = 16;
private readonly long[] _shards = new long[Environment.ProcessorCount * Spacing];
public void Inc() => Interlocked.Increment(ref _shards[(Environment.CurrentManagedThreadId % Environment.ProcessorCount) * Spacing]);
public long Read() { long t = 0; for (var i = 0; i < _shards.Length; i += Spacing) t += Volatile.Read(ref _shards[i]); return t; }
```

### Linked cancellation

```csharp
using var perRequest = CancellationTokenSource.CreateLinkedTokenSource(outer);
perRequest.CancelAfter(TimeSpan.FromSeconds(30));
await DoAsync(perRequest.Token);
```

### Bounded channel producer/consumer

```csharp
var ch = Channel.CreateBounded<T>(new BoundedChannelOptions(1024) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
// producer
await ch.Writer.WriteAsync(item, ct);
// consumer
await foreach (var item in ch.Reader.ReadAllAsync(ct)) Process(item);
ch.Writer.Complete();
```

### Resilient HTTP call (Polly v8)

```csharp
var pipe = new ResiliencePipelineBuilder()
    .AddTimeout(TimeSpan.FromSeconds(5))
    .AddRetry(new() { MaxRetryAttempts = 2 })
    .AddCircuitBreaker(new() { FailureRatio = 0.5, MinimumThroughput = 10, SamplingDuration = TimeSpan.FromSeconds(30) })
    .Build();
var result = await pipe.ExecuteAsync(async ct => await CallAsync(ct), ct);
```

## Don't list

- `lock(this)` / `lock(typeof(X))` / `lock("string")`
- `async void` (except event handlers)
- `.Result` / `.Wait()` / `.GetAwaiter().GetResult()`
- `Task.Factory.StartNew(..., LongRunning, ...)` for `async` work
- `Parallel.ForEach` over async
- Unbounded channels with no rate-limit on the producer
- `volatile` on `long`/`double` (use `Interlocked.Read` / `Volatile.Read`)
- `lock` around an `await`
- `ConcurrentBag<T>` as a general-purpose queue
- `BlockingCollection<T>` in async code (use `Channel<T>`)
