# 16 — Modern .NET Features (.NET 8 / 9 / 10)

> **Layer:** BCL + runtime
> **Reading time:** ~25 minutes

This chapter covers the concurrency-relevant additions in modern .NET. They each replace older idioms — knowing what's new keeps your code on the fast / safe path.

## In-chapter folders

| Folder | Topic | Since |
|---|---|---|
| [RateLimiting](RateLimiting/) | `System.Threading.RateLimiting` (4 algorithms + partitioning) | .NET 7 / 8 |
| [TimeProvider](TimeProvider/) | Testable abstraction over `DateTimeOffset.UtcNow` and `Task.Delay` | .NET 8 |
| [FrozenCollections](FrozenCollections/) | `FrozenDictionary<K,V>`, `FrozenSet<T>` — immutable read-optimised | .NET 8 |
| [AsyncMethodBuilder](AsyncMethodBuilder/) | Custom builders, pooled state machines | .NET 7 |
| [VirtualThreadsFuture](VirtualThreadsFuture/) | Speculation about Loom-like constructs in .NET | future |
| [NativeAOT](NativeAOT/) | AOT-compiled .NET and concurrency caveats | .NET 7 / 8 / 9 |

## Highlights summary

### `System.Threading.Lock` (.NET 9)

A real reference-type lock. Use over `lock(object)` in new code:

```csharp
private readonly Lock _sync = new();
lock (_sync) { /* … */ }
// or:
using (_sync.EnterScope()) { /* … */ }
```

### `Task.WhenEach` (.NET 9)

Streaming completion order:

```csharp
await foreach (var t in Task.WhenEach(tasks))
    var x = await t;            // already complete
```

### `ConfigureAwaitOptions` (.NET 8)

`SuppressThrowing`, `ForceYielding`. See [08/ConfigureAwait](../08-Async-Await-Deep-Dive/ConfigureAwait/).

### `System.Threading.RateLimiting` (.NET 7+)

First-class rate limiters: token bucket, sliding window, fixed window, concurrency. See [RateLimiting](RateLimiting/).

### Tier-1 dynamic PGO (.NET 8 default)

Profile-guided optimisations applied at tier-1 JIT. Devirtualises hot virtual calls, inlines hot paths. **No code change needed** — your code is faster on .NET 8+ than on 7-.

### `Parallel.ForEachAsync` (.NET 6+)

Already deeply covered in [07/Parallel.ForEachAsync](../07-Task-Parallel-Library/Parallel.ForEachAsync/). The right answer for IO fan-out.

### Pooled async state machines (.NET 7+)

`[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]` per method, or `DOTNET_SYSTEM_THREADING_POOLASYNCVALUETASKS=1` process-wide. Allocation-free async even on suspending paths.

### `ConcurrentDictionary.GetAlternateLookup<TAlternateKey>` (.NET 9)

Lookup with a different key type than the dictionary's:

```csharp
var dict = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
var alt = dict.GetAlternateLookup<ReadOnlySpan<char>>();
if (alt.TryGetValue(span, out var v)) { /* … */ }      // no string allocation
```

Removes one of the most common allocation hotspots: lookup-with-string-key.

## Run

```bash
dotnet run --project 16-Modern-.NET-Features
```
