# Immutability in .NET

Modern .NET gives you several distinct flavours of "immutable", each with different trade-offs. Knowing which one to reach for is the difference between a snappy and a slow concurrent design.

## Records — value-based scalar types

```csharp
public record Configuration(string Endpoint, int MaxRetries);

var a = new Configuration("https://api.example.com", 3);
var b = a with { MaxRetries = 5 };   // b is a new instance; a unchanged
```

What records give you:

- Compiler-generated `Equals` and `GetHashCode` based on field values.
- A `with` expression for non-destructive mutation (returns a modified copy).
- A primary constructor that initialises read-only properties.
- A nicely-formatted `ToString` for free.

What they *don't* give you:

- Immutability of reference-typed fields. If a record holds an `int[]`, the array is still mutable. You have to use `ImmutableArray<int>` (or a `ReadOnlyCollection`) for full immutability.

Use records for: configuration, DTOs, messages, value-typed snapshots.

## Immutable collections — share by reference, mutate by copy

```csharp
ImmutableArray<int> primes = [2, 3, 5, 7];
ImmutableArray<int> withEleven = primes.Add(11);   // primes unchanged
```

The `System.Collections.Immutable` namespace gives you:

- `ImmutableArray<T>` — wraps a `T[]`. Read-only; "modifying" operations return a new array.
- `ImmutableList<T>` — backed by a balanced tree (so `Add` is O(log n), not O(n)).
- `ImmutableDictionary<K,V>` — backed by an AVL tree.
- `ImmutableHashSet<T>`, `ImmutableSortedDictionary<K,V>`, `ImmutableQueue<T>`, …

The trade-off is performance per op:

| Operation | `List<T>` | `ImmutableArray<T>` | `ImmutableList<T>` |
|---|---|---|---|
| Random read | O(1) | O(1) | O(log n) |
| Add | O(1) amortised | O(n) (copies the array) | O(log n) |
| Bulk add | O(k) amortised | O(n + k) | O(k log n) |

If you frequently mutate small collections that are then "frozen", consider building with `ImmutableArray<T>.Builder` and `ToImmutable()`:

```csharp
var builder = ImmutableArray.CreateBuilder<int>();
for (int i = 0; i < 1000; i++) builder.Add(i);
var snapshot = builder.ToImmutable();   // single allocation
```

## Frozen collections (.NET 8+) — built once, read forever

```csharp
var byId = users.ToFrozenDictionary(u => u.Id);
// FrozenDictionary<int, User>
```

`FrozenDictionary` and `FrozenSet` are optimised for *only* read operations. They invest in expensive construction (analyse the keys, pick the best hash strategy, maybe switch to a perfect hash) and pay it back on every subsequent lookup. Benchmarks show 2-3× faster lookups than `Dictionary<K,V>` for typical workloads.

When to reach for them:

- The collection is built once at startup (or rarely) and read continuously.
- The keys are known and don't change.

Anti-pattern: building a `FrozenDictionary` per request. The construction cost dominates.

## Atomic publication

When you do need mutable shared state, the trick is to mutate *by replacing the whole object*. The replacement is one atomic reference write, observable as either old or new but never partial.

```csharp
private static ImmutableDictionary<int, User> _users = ImmutableDictionary<int, User>.Empty;

public static User? Get(int id) =>
    Volatile.Read(ref _users).GetValueOrDefault(id);

public static void Set(User user)
{
    while (true)
    {
        var snapshot = Volatile.Read(ref _users);
        var next = snapshot.SetItem(user.Id, user);
        if (Interlocked.CompareExchange(ref _users, next, snapshot) == snapshot)
            return;
        // CAS failed — another writer beat us. Retry.
    }
}
```

This is a **lock-free CAS loop** with an immutable backing store. Readers are never blocked. Writers retry on conflict — fine when contention is low; problematic if many writers hammer the same key (livelock-ish, but progress is still guaranteed because *some* writer always wins each round).

For most application code with one or two writers, `ImmutableDictionary` + atomic replacement is the sweet spot. For genuine many-writer scenarios, `ConcurrentDictionary` (which uses fine-grained per-bucket locking) is usually faster.

## Defensive copies — not the same as immutability

```csharp
public Configuration GetConfig() => _config;                  // hands out a reference
public Configuration GetConfig() => _config with { };         // hands out a copy
public Configuration GetConfig() => new(_config.E, _config.M); // hands out a copy
```

If `Configuration` is immutable, all three are equivalent and safe. If it's mutable, only the copy is safe — and the caller can still mutate *its* copy without affecting the original. This is "defensive copying" and it's a workaround for non-immutable types. Better: make the type immutable.

## A practical heuristic

> Make the type immutable. If you can't, confine it to one thread. If you can't, make changes lock-free via atomic publication. If you can't, lock it. If you can't, you have a design problem, not an implementation one.

In that order.
