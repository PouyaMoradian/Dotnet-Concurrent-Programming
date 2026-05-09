# Memory barriers

A *barrier* (or *fence*) is an instruction that constrains how reads/writes can be reordered around it. .NET exposes:

| API | Strength | Use |
|---|---|---|
| `Volatile.Read(ref x)` | acquire | most reads |
| `Volatile.Write(ref x, v)` | release | most writes |
| `Interlocked.*` | full fence (x86) / equivalent (ARM) | atomic ops |
| `Interlocked.MemoryBarrier()` | full fence | dekker-like patterns |
| `Thread.MemoryBarrier()` | alias for the above | older code |
| `Interlocked.MemoryBarrierProcessWide()` (.NET 5+) | flush across all cores | rare |

## When to use a full barrier

Almost never explicitly. The `Volatile` and `Interlocked` family already insert appropriate barriers. The classic case for an explicit `MemoryBarrier`: you have a non-volatile store followed by a non-volatile load, and you need StoreLoad ordering between them. Almost any time you'd write that, you'd be better off making the store and load both volatile.

## Reasoning about a barrier-correct program

Two questions:

1. **Pairing.** Every release-store should pair with an acquire-load on the same location. Otherwise the ordering is unenforced.
2. **Visibility.** "Thread B saw the new value" doesn't imply "Thread B saw all prior writes" — unless the new value was published with release semantics.

Common pattern — **double-checked locking** for a singleton:

```csharp
private static volatile Lazy<Singleton>? _lazy;

public static Singleton Instance
{
    get
    {
        var lazy = Volatile.Read(ref _lazy);     // acquire
        if (lazy is null)
        {
            var fresh = new Lazy<Singleton>(() => new Singleton(),
                                             LazyThreadSafetyMode.ExecutionAndPublication);
            lazy = Interlocked.CompareExchange(ref _lazy, fresh, null) ?? fresh;  // release on win
        }
        return lazy.Value;
    }
}
```

Better still: just `private static readonly Lazy<Singleton> _lazy = new(() => new Singleton());`. The CLR's static-init guarantees + Lazy<T>'s thread safety mode gives you the same semantics with less code.

## Process-wide barrier

`Interlocked.MemoryBarrierProcessWide()` (.NET 5+) issues a system call that broadcasts a barrier to *every* CPU executing the process. Useful for very-rare patterns where one writer pays the system-call cost so countless readers don't need to:

```csharp
// asymmetric synchronisation: cheap reads, expensive writes
ReadFastPath();
// (no barrier on the read side at all)

void Update()
{
    Volatile.Write(ref _state, newValue);
    Interlocked.MemoryBarrierProcessWide();   // pin the new value into every CPU's view
}
```

This is the technique used inside the BCL for some very hot code paths. **Not for application code.**

## Don't reach for barriers when you can lock

A `lock` is hundreds of nanoseconds and trivially correct. A hand-tuned barrier-laden CAS retry loop is tens of nanoseconds and easy to get wrong. Pick correctness; optimise after measurement.
