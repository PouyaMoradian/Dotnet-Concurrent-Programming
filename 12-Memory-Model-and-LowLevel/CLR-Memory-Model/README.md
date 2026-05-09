# The CLR memory model

Defined in **ECMA-335 §I.12.6** and clarified by Microsoft's *.NET Memory Model* (the consolidated description). The model is *stricter than C++'s* relaxed model and *weaker than full sequential consistency*.

## The guarantees

1. **Atomic reads/writes for aligned, pointer-sized-or-smaller types.** `int`, `bool`, `char`, `short`, `IntPtr`, `object` references — torn reads forbidden on aligned access. `long`/`double` on 32-bit are *not* atomic — use `Interlocked.Read` or `Volatile.Read` (.NET 7+).
2. **Volatile.Write is a release**: no preceding read or write may be reordered past it.
3. **Volatile.Read is an acquire**: no following read or write may be reordered before it.
4. **Interlocked operations are full fences** on x86 and have equivalent ordering on ARM64.
5. **`lock`/`Monitor.Enter` is an acquire; `Monitor.Exit` is a release.**
6. **Constructor stores happen-before any thread can observe a reference** — IF the reference was published via a release-store. Without release, a half-constructed object is observable.

## What the JIT may NOT do

- Move a write past a `Volatile.Write` or out of a `lock` block.
- Move a read before a `Volatile.Read` or above a `lock` enter.
- Reorder reads/writes across an `Interlocked.*` call.
- Eliminate volatile reads (it has to actually read the location every time).

## What the JIT may do

- Reorder independent non-volatile reads/writes within a method.
- Eliminate redundant non-volatile reads of the same location (caching in registers).
- Hoist invariant non-volatile reads out of loops.

This is why a polling loop on a non-volatile flag is broken: the JIT reads it once and keeps it in a register forever.

```csharp
// ❌ JIT can hoist `_stop` to a register; loop becomes infinite even if flag set
while (!_stop) { … }

// ✅ Volatile.Read on each iteration; can never be hoisted
while (!Volatile.Read(ref _stop)) { … }
```

## The publication idiom

```csharp
// Producer
_data = ComputeExpensive();
Volatile.Write(ref _ready, true);          // release; orders prior writes before this one

// Consumer
if (Volatile.Read(ref _ready))              // acquire
{
    Use(_data);                             // 'happens-after' ComputeExpensive
}
```

## Comparison with other models

| Model | "Default" ordering | Provides |
|---|---|---|
| Java (post-JSR-133) | volatile = full acquire/release; locks = acquire/release | similar to .NET, slightly stronger |
| C++11 `memory_order_seq_cst` | sequentially consistent | strongest, most expensive |
| C++11 `memory_order_relaxed` | none | fastest, very dangerous |
| .NET `Volatile.Read`/`Write` | acquire / release | between C++ acquire/release and seq_cst |
| .NET `Interlocked.*` | full fence on x86 | seq_cst-ish |

In practice, **stick to `Volatile` + `Interlocked` + `lock`**, and you're in well-defined territory on every supported platform.

## Reference

- **ECMA-335 §I.12.6** — the spec.
- **Vance Morrison: "What every developer must know about multithreaded apps"** — the introductory text.
- **Joe Duffy: "Concurrent Programming on Windows"** ch. 10 — the deepest .NET-specific treatment.
- **Hans Boehm: "Threads cannot be implemented as a library"** — why memory models exist at all.
