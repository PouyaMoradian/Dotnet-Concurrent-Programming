# Frozen collections (.NET 8)

`System.Collections.Frozen.FrozenDictionary<K,V>` and `FrozenSet<T>` are *build-once, read-many* immutable collections. The constructor analyses the data and picks the fastest possible read implementation.

## Performance

For typical lookups, expect:

| Vs. | Speedup |
|---|---|
| `Dictionary<TKey,TValue>` | 2–5× |
| `ConcurrentDictionary<TKey,TValue>` | 5–20× (the latter has lock checks) |
| `ImmutableDictionary<TKey,TValue>` | 10–50× (the latter is a tree) |

The price is **construction cost**: 10–100× slower to build than `Dictionary<K,V>`. The break-even is somewhere around hundreds of reads per build. For a settings dictionary read once per request and built at startup, the math is overwhelming.

## Building

```csharp
var data = LoadFromConfig();                          // IEnumerable<KeyValuePair<string, T>>
FrozenDictionary<string, T> dict = data.ToFrozenDictionary();
```

For specific comparers:

```csharp
var dict = data.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
```

## What goes inside

The runtime implementation chooses between:

- **Single-element optimisations** for small dictionaries (1–10 entries).
- **Length-bucketed string lookups** when keys are strings of varying length.
- **Specialised integer hash maps** with reduced collisions.
- **Default open-addressing hash table** as a fallback.

You don't see this; you just enjoy the speedup. As .NET versions advance, the heuristics get smarter; rebuilding on a newer runtime is free perf.

## Concurrency angle

Frozen collections are **immutable**, so reads are *thread-safe by virtue of immutability*. No synchronisation required. They're the perfect fit for "configured at startup, read on every request" data:

- Routing tables.
- Feature flag → handler mapping.
- Role → permission set lookups.
- Country code → tax rate.

For data that *might* change rarely, the **CoW** pattern works on top:

```csharp
private static FrozenDictionary<string, RouteConfig> _routes = Empty;

public static FrozenDictionary<string, RouteConfig> Routes => Volatile.Read(ref _routes);

public static void Reload(IEnumerable<KeyValuePair<string, RouteConfig>> next)
    => Volatile.Write(ref _routes, next.ToFrozenDictionary());
```

Readers are lock-free *and* read-optimal. The reload is a full rebuild — but rare. Best for "once an hour" reload patterns.

## When NOT to use Frozen

- **Frequently mutated data.** Build cost is too high.
- **Large dynamic sets** that grow during the lifetime of the process.
- **Tiny short-lived dictionaries** built and used a few times — `Dictionary` is faster end-to-end.

## A quick benchmark snippet

```csharp
[MemoryDiagnoser]
public class FrozenBench
{
    private Dictionary<string, int> _dict = null!;
    private FrozenDictionary<string, int> _frozen = null!;
    private string[] _keys = null!;

    [GlobalSetup]
    public void Setup()
    {
        _keys = Enumerable.Range(0, 10_000).Select(i => $"key_{i}").ToArray();
        _dict = _keys.ToDictionary(k => k, k => k.Length);
        _frozen = _dict.ToFrozenDictionary();
    }

    [Benchmark(Baseline = true)] public int Dict() { var s=0; foreach (var k in _keys) s += _dict[k]; return s; }
    [Benchmark]                  public int Frozen() { var s=0; foreach (var k in _keys) s += _frozen[k]; return s; }
}
```

Typical result on .NET 10: Frozen is 3–5× faster for string-keyed maps in this size.
