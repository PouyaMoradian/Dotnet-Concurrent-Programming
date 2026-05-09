# 05 — Atomic Operations

> **Layer:** CPU + CLR
> **Reading time:** ~30 minutes
> **Prereq:** [00](../00-Prerequisites/), [04](../04-Synchronization-Primitives/), at least skim [12-Memory-Model](../12-Memory-Model-and-LowLevel/) afterwards

Atomics let you build correct concurrent code *without* a lock. A single atomic instruction (CAS, exchange, increment) is implemented in hardware as one indivisible operation visible to all cores. They are:

- **Faster than locking** when contention is low or moderate.
- **Equivalent in cost** to a contended lock on a single hot field (because the cache-coherence traffic is the actual bottleneck, not the lock overhead).
- **Necessary** for lock-free data structures.
- **Easy to misuse** — most lock-free code from blogs is subtly wrong (ABA, missing fences, ordering).

## .NET's atomic API

| Class | What it offers |
|---|---|
| `System.Threading.Interlocked` | `Increment`, `Decrement`, `Add`, `Exchange`, `CompareExchange`, `Read` (long on 32-bit), `Or`/`And`/`Xor` (.NET 5+), `MemoryBarrier` |
| `System.Threading.Volatile` | `Read`, `Write` with acquire/release semantics |
| `System.Runtime.Intrinsics.X86.*` etc. | Direct CPU intrinsics (rarely needed) |

## In-chapter folders

| Folder | Topic |
|---|---|
| [Interlocked](Interlocked/) | The atomic operations API: what each does, when to use it |
| [Volatile](Volatile/) | `Volatile.Read/Write` and the volatile keyword: when each applies |
| [CompareExchange](CompareExchange/) | The CAS primitive that everything lock-free is built on |
| [LockFreePatterns](LockFreePatterns/) | Single-writer publishing, lock-free counters, treiber stack |
| [ABA-Problem](ABA-Problem/) | The classic CAS pitfall and how to avoid it |

## Quick examples

```csharp
// Atomic counter — orders of magnitude faster than lock at low contention.
Interlocked.Increment(ref _count);

// Atomic publish — make a new state visible without locking.
Interlocked.Exchange(ref _config, newConfig);

// CAS-based update — retry until we win.
SnapshotState old, next;
do
{
    old = _state;
    next = Compute(old);
} while (Interlocked.CompareExchange(ref _state, next, old) != old);

// Volatile read — guarantees we see the latest publish (within the memory model rules).
var snapshot = Volatile.Read(ref _state);
```

## When NOT to use atomics

- **More than one variable to update atomically.** Atomics are *single-location* primitives. If you need to update two fields together, you either need a lock, or to pack them into one cache-line-aligned struct accessed via `CompareExchange<T>` (which has limits — `T` must be a reference or a struct ≤ pointer size for the cheap path).
- **Complex critical sections.** A `lock` is far easier to read.
- **As a substitute for a real data structure.** "I'll just use Interlocked.Increment everywhere" usually has a better answer (ConcurrentDictionary / Channel).

## Memory ordering — preview of [12](../12-Memory-Model-and-LowLevel/)

Most `Interlocked` operations on x86 imply a *full memory fence*. That gives you sequential consistency around them. But on ARM64, the JIT inserts explicit `dmb ish` barriers — and only when needed. **You don't need to think about the ISA differences if you stick to `Interlocked` and `Volatile`.** You *do* need to think about them if you write inline assembly or use `System.Runtime.Intrinsics` directly.

## Run

```bash
dotnet run --project 05-Atomic-Operations
```
