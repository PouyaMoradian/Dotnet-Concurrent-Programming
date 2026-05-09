# 12 — Memory Model and Low-Level

> **Layer:** CPU + JIT + CLR
> **Reading time:** ~40 minutes
> **Prereq:** [00](../00-Prerequisites/), [05](../05-Atomic-Operations/)

The memory model is the *contract* between you, the JIT, and the CPU about how memory operations from one thread become visible to another. Understanding it lets you write lock-free code that's correct on x86 *and* ARM64 *and* future architectures — without superstition.

## In-chapter folders

| Folder | Topic |
|---|---|
| [CLR-Memory-Model](CLR-Memory-Model/) | The .NET memory model, ECMA spec, how it relates to C++/Java |
| [CPU-Reordering](CPU-Reordering/) | TSO (x86), weakly-ordered (ARM64), what reorders and why |
| [MemoryBarriers](MemoryBarriers/) | `MemoryBarrier`, fences, when each is needed |
| [Unsafe](Unsafe/) | `Unsafe` static class — when to use, when to run away |
| [HardwareIntrinsics](HardwareIntrinsics/) | `System.Runtime.Intrinsics.*` for vectorised compute |
| [SIMD](SIMD/) | `Vector<T>`, `Vector256<T>`, autovectorisation |
| [NativeInterop](NativeInterop/) | P/Invoke and concurrency: GC modes, blocking calls, marshalling |

## The CLR memory model in one paragraph

> Reads and writes of pointer-sized aligned data are atomic. Volatile reads have *acquire* semantics; volatile writes have *release* semantics. `Interlocked` operations include a full memory fence on x86 and the equivalent ordering on ARM64. The runtime guarantees that a happens-before relationship is established between a release-store and a subsequent acquire-load on the same location.

That paragraph, plus this reading list, is enough to write correct lock-free .NET code.

## What the JIT can reorder

| Scenario | JIT may reorder? |
|---|---|
| Two independent reads of different fields | yes |
| Two independent writes of different fields | yes (unless `volatile` / `Volatile.Write`) |
| Read after write of same field | no |
| Across `Volatile.Read`/`Volatile.Write` | no (release/acquire are barriers) |
| Across `Interlocked.*` | no (full fences) |
| Across `lock { … }` boundaries | no (acquire on enter, release on exit) |

## What the CPU can reorder

x86 (TSO):

- Stores can be delayed past later loads (`StoreLoad` reordering allowed).
- `LoadLoad`, `StoreStore`, `LoadStore`: forbidden.
- `mfence` / `lock`-prefixed instructions block `StoreLoad`.

ARM64 (weakly ordered):

- Almost any reordering allowed.
- `LDAR` (load-acquire), `STLR` (store-release), `DMB ISH` (full fence) provide ordering.
- `Interlocked` operations emit appropriate fences.

The CLR shields you from these differences as long as you use `Volatile`/`Interlocked`.

## Run

```bash
dotnet run --project 12-Memory-Model-and-LowLevel
```
