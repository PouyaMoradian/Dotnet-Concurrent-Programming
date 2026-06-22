# .NET tools — picking the right barrier

The .NET BCL gives you five tools for memory-visibility problems, in roughly ascending order of strength and cost. Pick the cheapest one that covers your case.

## 1. `volatile` (the C# keyword)

```csharp
private volatile bool _ready;
// ...
_ready = true;        // release write
while (!_ready) { }   // acquire read each iteration
```

Pros:

- Zero-friction syntax. Apply at the field declaration; every read/write through the field is automatically a volatile access.
- The JIT enforces no register caching across the read.

Cons:

- Doesn't work on `long`, `double`, or structs — only on word-sized-or-smaller value types (`bool`, `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `char`, `float`, enums with one of those as the underlying type, `IntPtr`/`UIntPtr`, pointers) and reference types. The compiler errors on the unsupported cases (good!).
- Provides release-acquire only; not a full barrier.
- Less explicit than `Volatile.Read/Write` at the call site, which can make debugging harder.

When to use: simple boolean / int flags that are read or written from multiple threads, where you don't care about full-barrier semantics.

## 2. `Volatile.Read<T>` / `Volatile.Write<T>`

```csharp
private int _ready;
// ...
Volatile.Write(ref _ready, 1);
while (Volatile.Read(ref _ready) == 0) { }
```

Same semantics as `volatile`, but explicit at the call site. Works on the same set of types as `volatile`.

Pros:

- Explicit: anyone reading the call site can see "this is a memory-ordered access."
- Doesn't require modifying the field's declaration — useful in libraries where you want some accesses ordered and others not.

Cons:

- More to type. Easy to forget the `Volatile.Write` on one side of the contract.

When to use: most production code that doesn't want the `volatile` keyword at the field level. This is the modern .NET idiom.

## 3. `Interlocked.*` operations

```csharp
Interlocked.Increment(ref _counter);
Interlocked.Exchange(ref _state, newState);
Interlocked.CompareExchange(ref _state, newState, expected);
long v = Interlocked.Read(ref _longField);
```

Atomic *and* a full memory barrier (with some exceptions: `Read` is acquire-only on some implementations).

Pros:

- Atomic — no torn reads even for `long` on 32-bit.
- Full barrier — covers more reorderings than `Volatile`.
- Lock-free under contention — no thread blocks.

Cons:

- More expensive per operation than `Volatile` (a few tens of nanoseconds).
- Only works on aligned numeric types and references.
- CAS loops can starve writers under high contention (livelock-ish, but with progress).

When to use: counters, flags that need atomic compound updates (set-if-zero, swap), publishing references atomically.

## 4. `lock(obj)` / `Monitor.Enter` / `Monitor.Exit`

```csharp
private readonly object _gate = new();
// ...
lock (_gate)
{
    _state.Value++;
    _state.Counter++;
}
```

Acquire on entry, release on exit. The body runs in mutual exclusion with every other `lock(_gate)`.

Pros:

- Easy to reason about. Sequential code semantics inside the body.
- Covers compound mutations naturally.
- The C# `lock` statement compiles to a try/finally that always releases.

Cons:

- Serial bottleneck under contention.
- Can deadlock if locks are taken in different orders by different threads.
- Doesn't compose well — locking two objects requires care to avoid deadlock; recursion can hit `Monitor.PulseAll` surprises.

When to use: anything that needs more than a single atomic update. The default tool when in doubt.

In .NET 9+, the C# `lock` statement on a field of type `System.Threading.Lock` uses a new low-overhead lock primitive that's measurably cheaper than `Monitor`. Prefer it for new code that targets net9.0+.

## 5. `Thread.MemoryBarrier()` / `Interlocked.MemoryBarrier()`

```csharp
_data = 42;
Thread.MemoryBarrier();
_ready = true;
```

The nuclear option. A full bidirectional barrier: no read or write can be reordered across this line, by the JIT or the CPU.

Pros:

- Covers any scenario the others don't.

Cons:

- Costly: drains the store buffer on x86 (a few dozen cycles).
- Usually not needed. If you find yourself reaching for `Thread.MemoryBarrier()`, double-check that `Volatile.Read/Write` or `Interlocked` aren't sufficient.

When to use: rare. Implementing lock-free data structures from scratch. Implementing a custom synchronization primitive. Most application code never touches it.

## The decision flow

```
Do I need atomic compound updates (CAS, increment under contention)?
  → Interlocked.*

Do I need to protect a multi-field invariant or run multiple operations atomically?
  → lock

Do I need just a release-acquire on a single int / bool / reference?
  → Volatile.Read / Volatile.Write
    (or the volatile keyword if the entire field is always accessed this way)

Do I really, truly need a full bidirectional barrier with no other operation?
  → Thread.MemoryBarrier
    (rare; usually one of the above is closer to what you want)

Is the field shared but only ever read?
  → No barrier needed once the field is initialised. Make it readonly.
```

## A worked example: the "publish an immutable snapshot" pattern

This is the most common production pattern. We have a cache that gets replaced occasionally; readers want the latest version, writers want to publish atomically.

```csharp
public sealed class SnapshotCache<TKey, TValue> where TKey : notnull
{
    private ImmutableDictionary<TKey, TValue> _snapshot = ImmutableDictionary<TKey, TValue>.Empty;

    public TValue? Get(TKey key) =>
        Volatile.Read(ref _snapshot).TryGetValue(key, out var v) ? v : default;

    public void Update(Func<ImmutableDictionary<TKey, TValue>, ImmutableDictionary<TKey, TValue>> mutate)
    {
        while (true)
        {
            var before = Volatile.Read(ref _snapshot);
            var after  = mutate(before);
            if (Interlocked.CompareExchange(ref _snapshot, after, before) == before) return;
        }
    }
}
```

Each tool here is doing one job:

- `Volatile.Read(ref _snapshot)` gives the reader an acquire-load, ensuring it sees the latest published snapshot and everything that was visible *before* the publication.
- `Interlocked.CompareExchange` publishes the new snapshot atomically and with full-barrier semantics — any writes the mutator made into the new dictionary are visible to subsequent readers.
- The `while (true)` retry handles the case where two writers race; the loser sees its CAS fail and retries against the new snapshot.

No `lock` needed. No `Thread.MemoryBarrier` needed. The cheapest correct primitive in each spot.
