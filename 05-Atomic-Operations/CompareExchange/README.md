# CompareExchange — the universal atomic primitive

`Interlocked.CompareExchange(ref location, newValue, expected)` does:

> If `location == expected`, set `location = newValue`. Return the *old* value of `location` (whether or not we succeeded).

Implemented in hardware as `lock cmpxchg` (x86) or an `LDXR`/`STXR` retry loop (ARM64). It is *the* universal atomic primitive — any other atomic operation can be built on top of it.

## The retry-loop pattern

```csharp
T old, next;
do
{
    old = Volatile.Read(ref _state);
    next = Compute(old);                    // pure: no side effects
} while (Interlocked.CompareExchange(ref _state, next, old) != old);
```

Three rules for this pattern:

1. **`Compute` must be pure.** It runs once per attempt; side effects would replay.
2. **No deadlock**: every iteration makes progress somewhere (you make progress, or someone else did).
3. **Bounded contention or use SpinWait**: under heavy contention, throw a `SpinWait.SpinOnce()` in the loop body to back off.

## Reference-typed CAS

The overload `CompareExchange<T>(ref T location, T value, T comparand) where T : class` lets you swap references atomically. This is how lock-free linked structures (Treiber stack, Michael-Scott queue) work.

```csharp
// Insert at head of singly-linked list, lock-free.
public void Push(Node n)
{
    Node? old;
    do
    {
        old = Volatile.Read(ref _head);
        n.Next = old;
    } while (Interlocked.CompareExchange(ref _head, n, old) != old);
}
```

## CAS on a struct value

For larger atomic units (e.g., `(version, value)` to defeat ABA), pack into a struct ≤16 bytes and use `Interlocked.CompareExchange<T>(ref T)` where `T` is the struct. The runtime handles the implementation: 8-byte structs use `cmpxchg8b`, 16-byte structs use `cmpxchg16b` on x86. ARM64 uses `LDAXP/STLXP` instruction pairs.

```csharp
// Tagged pointer for ABA-safe stack.
public readonly record struct TaggedNode(Node? Node, ulong Tag);
TaggedNode current = Volatile.Read(ref _top);   // read both atomically — *if* the struct read is atomic
```

Note: `Volatile.Read` of a 16-byte struct is *not* generally atomic — for atomic read of a `TaggedNode` you typically use `Interlocked.CompareExchange(ref _top, _top, _top)` (a no-op CAS) or a `Read` helper.

## Common bug: wrong comparand

```csharp
// ❌ Captured-by-value comparand bug.
var copy = _state;
ProcessAsync(copy).ContinueWith(t =>
{
    Interlocked.CompareExchange(ref _state, t.Result, copy); // 'copy' may be very stale by now
});
```

If many concurrent updaters are at play, your comparand needs to be *fresh* — typically captured immediately before the CAS, not seconds before.

## Performance

| Op | Latency |
|---|---|
| Uncontended CAS | ~10–20 ns |
| Contended CAS (one cache miss) | ~30–50 ns + cache-coherence cost |
| Heavily-contended CAS (many cores writing same line) | up to µs |

For *very* heavily contended single locations, **CAS does not save you**. The bottleneck is cache coherence, not the lock. The fix is to remove the contention (sharding, per-thread state, lock-free designs that don't centralise on one location).
