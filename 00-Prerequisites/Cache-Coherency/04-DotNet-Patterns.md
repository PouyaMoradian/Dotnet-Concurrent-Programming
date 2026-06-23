# Cache-coherence-friendly .NET patterns

The previous notes explained *what* the protocol does and *why* writes are expensive. This note is the practical follow-through: the patterns that work with the protocol and the ones that fight it. None of these is exotic; you'll find every one in the **BCL** (Base Class Library — the `System.*` types shipped with .NET) itself, used for exactly the reason we describe.

## Pattern 1 — Shard the write set

The single most reusable idea: replace one contended line with N independent lines.

### `ThreadLocal<T>`

```csharp
private static readonly ThreadLocal<long> _hits = new(() => 0L, trackAllValues: true);

public void Hit() => _hits.Value++;

public long Total => _hits.Values.Sum();   // aggregate across all threads
```

`ThreadLocal<T>` allocates a wrapper per thread; `Values` collects them. Use this when threads come and go and you don't want to manage indices yourself.

### `Parallel.For` with `localInit` / `localFinally`

The `Parallel` overload that takes a per-task initialiser and finaliser:

```csharp
long total = 0;
Parallel.For(0, items.Length,
    localInit: () => 0L,
    body: (i, _, local) => local + Work(items[i]),
    localFinally: local => Interlocked.Add(ref total, local));
```

Each worker keeps a private `local`. Only the final `Interlocked.Add` touches the shared line — once per worker, not once per item.

### Hand-rolled per-core sharding

When you know exactly how many shards you want:

```csharp
[StructLayout(LayoutKind.Explicit, Size = 128)]   // pad to two cache lines
struct PaddedLong { [FieldOffset(0)] public long Value; }

public sealed class ShardedCounter
{
    private readonly PaddedLong[] _shards = new PaddedLong[Environment.ProcessorCount];

    public void Increment()
    {
        int slot = Thread.GetCurrentProcessorId() % _shards.Length;
        Interlocked.Increment(ref _shards[slot].Value);
    }

    public long Sum()
    {
        long s = 0;
        for (int i = 0; i < _shards.Length; i++)
            s += Interlocked.Read(ref _shards[i].Value);
        return s;
    }
}
```

Note the `PaddedLong` struct: without padding, two shards could share a 64-byte line and you'd be back to false sharing. With 128 B (covering Apple Silicon's larger line) you're safe everywhere.

## Pattern 2 — Immutable reads

If readers never mutate, they can hold the line in S forever:

```csharp
private volatile Dictionary<string, Config> _config = LoadConfig();   // start state

public Config Get(string key) => _config[key];   // pure read, no coherence traffic

public void Reload()
{
    var fresh = LoadConfig();
    _config = fresh;     // single publish; readers may briefly see the old or new map
}
```

Writers replace the *reference*, not the contents. Readers always see a consistent snapshot. The trade-off is allocation cost on rebuild and a small window where readers see the old version — usually a feature, not a bug.

This is also the design of `ImmutableDictionary<TKey, TValue>` and the F# functional collections: every modification produces a new structure that shares most of its nodes with the old; readers continue using the old reference without contention.

## Pattern 3 — Stripe the lock

When mutation is unavoidable, partition the data so each subset has its own lock:

```csharp
public sealed class StripedHashMap<TK, TV> where TK : notnull
{
    private readonly object[] _locks;
    private readonly Dictionary<TK, TV>[] _buckets;

    public StripedHashMap(int stripes = 64)
    {
        _locks = Enumerable.Range(0, stripes).Select(_ => new object()).ToArray();
        _buckets = Enumerable.Range(0, stripes).Select(_ => new Dictionary<TK, TV>()).ToArray();
    }

    private int Stripe(TK key) => (key.GetHashCode() & int.MaxValue) % _locks.Length;

    public TV Get(TK key) { lock (_locks[Stripe(key)]) return _buckets[Stripe(key)][key]; }
    public void Set(TK key, TV value)
    {
        var s = Stripe(key);
        lock (_locks[s]) _buckets[s][key] = value;
    }
}
```

This is essentially what `ConcurrentDictionary<TKey, TValue>` does internally (with finer locking and lock-free reads). The number of stripes should be at least `2 × ProcessorCount` to keep collision rates low.

## Pattern 4 — Lock-free read, locked write

The right design for many "mostly-read, occasionally-write" stores:

```csharp
public sealed class WriteOnceCache<TK, TV> where TK : notnull
{
    private ImmutableDictionary<TK, TV> _state = ImmutableDictionary<TK, TV>.Empty;

    public TV? TryGet(TK key) => _state.TryGetValue(key, out var v) ? v : default;

    public void Add(TK key, TV value)
    {
        while (true)
        {
            var prior = _state;
            var next = prior.Add(key, value);
            if (Interlocked.CompareExchange(ref _state, next, prior) == prior) return;
            // CAS (Compare-And-Swap) failed: someone else updated; retry against the new state.
        }
    }
}
```

Reads are a single field read — no synchronisation, just whatever the latest publish was. Writes pay an `Interlocked.CompareExchange` plus the cost of constructing a new `ImmutableDictionary` (cheap; structural sharing).

The classic mistake is to compose lock-free reads with non-atomic writes. Always pair `CompareExchange` with an immutable underlying state — otherwise you've just written a check-then-act bug.

## Pattern 5 — Batch the publish

When events are frequent but the consumer only needs the aggregate occasionally:

```csharp
public sealed class BatchedMetric
{
    [ThreadStatic] private static long _local;
    private long _shared;

    public void Hit() => _local++;     // L1 write, no contention

    public void Flush()
    {
        var delta = _local;
        _local = 0;
        if (delta != 0) Interlocked.Add(ref _shared, delta);
    }

    public long Read() => Interlocked.Read(ref _shared);
}
```

Call `Flush` from each thread on a timer (every 100 ms) or at the end of a request. Most increments never touch the shared line.

This is exactly the pattern `System.Diagnostics.Metrics.Counter<T>` uses internally (with more sophistication for tag dimensions).

## Pattern 6 — Use `Parallel.ForAsync` for IO with shared aggregates

For mixed IO + aggregation work, prefer the localInit/localFinally style or per-task lambdas with local state:

```csharp
var total = new ConcurrentBag<int>();
await Parallel.ForEachAsync(items, async (item, ct) =>
{
    int local = await ProcessAsync(item, ct);
    total.Add(local);   // ConcurrentBag is lock-free for adds
});
int sum = total.Sum();
```

`ConcurrentBag<T>` is itself sharded: each thread has its own local queue, and stealing happens only when a thread runs out of its own work.

## Anti-patterns to retire

### ❌ Counters on a shared static `long`
```csharp
private static long _requests;
public void OnRequest() => Interlocked.Increment(ref _requests);
```
Use a sharded counter or `System.Diagnostics.Metrics.Counter<long>`.

### ❌ Locking on a class instance
```csharp
lock (someInstance) { ... }
```
Anyone with a reference to `someInstance` can lock on it too. Lock on a private `readonly object`.

### ❌ `volatile` on a complex object reference
```csharp
private volatile MyObject _obj;
```
A volatile reference makes the *reference assignment* visible, not the contents. Other threads can still see half-constructed state if you mutate fields after publishing. Either initialise fully before publishing, or use an immutable type.

### ❌ Read-modify-write under `volatile`
```csharp
private volatile int _counter;
_counter++;   // NOT atomic! Three steps: read, add, write.
```
Use `Interlocked.Increment`.

## Diagnosing contention in production

| Tool | Signal |
|---|---|
| `dotnet-counters monitor System.Runtime` | `lock-contention-count` ticking |
| `dotnet-trace` with `Microsoft-DotNETCore-ContentionStart/Stop` | Per-lock contention events |
| `perf stat -e cache-misses,LLC-load-misses` (Linux) | High **LLC** (Last-Level Cache) miss rate on hot threads |
| BenchmarkDotNet `[HardwareCounters(HardwareCounter.CacheMisses)]` | Same, in a controlled microbench |
| Visual Studio Concurrency Visualizer | Per-thread blocked-on-sync timeline |

When you see contention numbers and need a fix, the rank order is usually: (1) is there a sharding opportunity? (2) can we make the read side lock-free with immutable state? (3) is the data partitionable so we can stripe the lock? (4) only then, fall back to optimising the critical section.

## Practical takeaways

- Always assume hot mutable shared state is the bottleneck until you prove otherwise.
- Sharding + aggregation is the workhorse pattern. Reach for it first.
- Immutable state is the only true "free lunch" in concurrency — readers don't fight.
- The BCL gives you the right tools (`ThreadLocal`, `ConcurrentDictionary`, `ImmutableDictionary`, `Parallel.For` localInit). Use them.

## Lab

`ContendedInterlockedDemo` (demo 7) compares the naive shared counter to a sharded variant, with both running over the same total work. Expect ~5–20× speed-up from sharding on an 8-core box.

## Further reading

- **Joe Duffy — *Concurrent Programming on Windows*** — chapter on contention is still the best in print.
- **Stephen Toub — *Performance improvements in .NET 7/8/9*** posts — many of the listed changes are exactly "removed a hot atomic on a shared line by sharding internally".
- **`ConcurrentDictionary<TKey, TValue>` source** — the reference implementation of striped locking + lock-free reads in .NET.
