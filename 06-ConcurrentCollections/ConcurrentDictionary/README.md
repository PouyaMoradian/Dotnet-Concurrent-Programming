# ConcurrentDictionary&lt;K, V&gt;

The most-used type in this namespace. Strikes the best read/write balance for general-purpose concurrent maps.

## Internals (post-.NET 5)

- A **table of buckets** with **striped locking**: each bucket has its own lock, so two writes hashing to different stripes do not contend.
- Read operations are **lock-free** — they read a snapshot of the bucket chain.
- Locks degrade to a `Monitor` when contended; no kernel waits for the common case.
- Default stripe count = `Environment.ProcessorCount * 4`. Configurable via the `concurrencyLevel` constructor argument.

## API points worth knowing

```csharp
TryAdd(key, value);                         // add iff absent
TryGetValue(key, out value);                // lock-free read
TryUpdate(key, newValue, comparand);        // CAS-style: only if existing == comparand
TryRemove(key, out value);                  // remove and return
GetOrAdd(key, factory);                     // get or compute-and-add
AddOrUpdate(key, addFactory, updateFactory);// add new or transform existing
```

## The `GetOrAdd` race trap

```csharp
dict.GetOrAdd(key, k => ExpensiveCreate(k));
```

**The factory may run multiple times under contention.** Only one return value wins (the rest are discarded). For most pure functions this is fine. For:

- Side-effecting factories (logging, registering)
- Expensive-to-build singletons
- Resources that need disposal

Wrap in `Lazy<T>`:

```csharp
private readonly ConcurrentDictionary<K, Lazy<V>> _map = new();

V Get(K key) => _map.GetOrAdd(key, k =>
    new Lazy<V>(() => Build(k), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
```

The `Lazy<T>` wrapper guarantees `Build` runs at most once per *winning* `Lazy<T>` instance.

## `AddOrUpdate` — atomic-ish

```csharp
dict.AddOrUpdate(
    key,
    addValueFactory: k => 1,
    updateValueFactory: (k, existing) => existing + 1);
```

Atomic at the bucket level. The two factories don't run together; one wins. Same caveat as `GetOrAdd` — under contention, the *update* factory may run more than once before a CAS succeeds.

## Enumeration

`foreach (var kvp in dict)` is **safe** but gives you a **snapshot view that may include or exclude concurrent updates**. Don't expect a consistent point-in-time view. For point-in-time views, take an explicit snapshot:

```csharp
var snapshot = dict.ToArray();   // allocates; thread-safe; consistent at the call boundary
```

## Sizing for performance

| Scenario | Hint |
|---|---|
| Small (<100 entries) read-mostly | Consider `FrozenDictionary` if build-once |
| Medium, mixed | Default `ConcurrentDictionary` |
| Very write-heavy | Reduce stripes (`concurrencyLevel`) — fewer locks but more contention; profile |
| Reads vastly dominate writes | Try `ImmutableDictionary` + atomic-swap |

## Pitfalls

1. **Reference-type values that you mutate after insertion**: the dictionary doesn't know. The contained object's thread safety is *your* problem.
2. **`Count` is racy** — exposed but expensive (it locks every stripe). Don't call in hot paths.
3. **`ContainsKey` then `[]`** — TOCTOU race. Use `TryGetValue`.
