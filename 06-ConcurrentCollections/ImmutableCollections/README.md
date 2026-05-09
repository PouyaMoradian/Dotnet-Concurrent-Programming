# Immutable & Frozen collections

Two libraries that look similar and serve very different purposes.

## `System.Collections.Immutable`

Persistent (functional) data structures. Every "mutation" returns a new collection that **shares structure** with the old one. Not free, but cheap (logarithmic per operation for trees; constant for arrays-with-spine).

```csharp
ImmutableList<int> a = [1, 2, 3];
ImmutableList<int> b = a.Add(4);    // a is unchanged; b shares all of a's nodes plus the new tail
```

Use when:

- **Reads vastly outnumber writes**, and readers need stable snapshots.
- You want to *atomically swap* the whole structure (CoW pattern for hot config / routing tables).
- You're modelling state in a CQRS / event-sourced system and want immutable historical states.

Don't use for:

- Hot mutate-in-place workloads. Even shared-structure copies allocate.

## `System.Collections.Frozen` (.NET 8+)

The set of "I built this once and now read it forever" types. The construction phase analyses the data and picks the fastest possible read implementation (perfect-hash-like for small sets, specialised string lookups by length, etc.).

```csharp
var fd = users.ToFrozenDictionary(u => u.Id);   // expensive to build, cheap to read
```

Read times are typically **2–10× faster** than `Dictionary<TKey, TValue>` and **5–20× faster** than `ConcurrentDictionary<TKey, TValue>`. There is no mutation API; you must build a new one to "change" anything.

Use when:

- The map is **populated once at startup** and read on hot paths thereafter.
- Routing tables, settings dictionaries, role-permission matrices, lookup tables.
- You'd otherwise reach for "Dictionary plus lock-because-no-one-mutates."

## Comparison

| Type | Mutation | Reader concurrency | Typical read latency | When |
|---|---|---|---|---|
| `Dictionary<K,V>` | yes (not thread-safe) | none | fastest of mutable | single-threaded |
| `ConcurrentDictionary<K,V>` | yes | striped | medium | mixed workloads |
| `ImmutableDictionary<K,V>` | copy-on-write | snapshot | slower than `Dictionary` | snapshotting, atomic-swap |
| `FrozenDictionary<K,V>` | none | snapshot | fastest | build-once / read-many |

## Atomic-swap CoW pattern

```csharp
private static ImmutableDictionary<string, RouteConfig> _routes =
    ImmutableDictionary<string, RouteConfig>.Empty;

public static ImmutableDictionary<string, RouteConfig> Routes => Volatile.Read(ref _routes);

public static void UpdateRoutes(Func<ImmutableDictionary<string, RouteConfig>,
                                     ImmutableDictionary<string, RouteConfig>> updater)
{
    ImmutableDictionary<string, RouteConfig> old, next;
    do { old = Routes; next = updater(old); }
    while (Interlocked.CompareExchange(ref _routes, next, old) != old);
}
```

Readers are lock-free. Writers retry under contention. **For a build-once-then-read pattern, `FrozenDictionary` is faster.** For a "rarely update, always read latest" pattern, the immutable + CAS combo is the right choice.

## Builders

`ImmutableArray<T>.Builder` / `ImmutableList<T>.Builder` etc. are mutable types you populate, then call `ToImmutable()` to freeze. Use them when constructing large immutable collections in steps — the builder is much faster than chained `Add`.
