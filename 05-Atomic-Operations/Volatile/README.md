# `Volatile` — release/acquire semantics

`Volatile.Read<T>(ref T)` and `Volatile.Write<T>(ref T, T)` are the lightweight ordering primitives. They do not lock anything; they only:

- **`Volatile.Read`**: an *acquire* — no subsequent reads/writes can be reordered before it.
- **`Volatile.Write`**: a *release* — no preceding reads/writes can be reordered after it.

Together, a release-store paired with an acquire-load establishes *happens-before* between two threads. The classic publication pattern:

```csharp
// Producer
_data = Build();                    // 1
Volatile.Write(ref _published, true); // 2 — release; orders 1 before this

// Consumer
if (Volatile.Read(ref _published))   // 3 — acquire; pairs with 2
{
    Use(_data);                      // 4 — guaranteed to see what 1 wrote
}
```

## Why not the `volatile` keyword?

C#'s `volatile` keyword:

- Is field-level (annoying to reason about; affects every access).
- **Does not work on `long`/`double`** (no torn-read protection on 32-bit; the language spec confines it to types that fit in a pointer-sized atomic load).
- Has subtly different ordering than `Volatile.Read/Write` in some old compiler versions.

`Volatile.Read/Write` is per-access, explicit, and works for all types (including 64-bit on 32-bit). **Always prefer `Volatile.Read/Write` over `volatile`.**

## When `Volatile` is enough

- **Single-writer publishing.** One thread writes, many threads read; you only need acquire-release.
- **Flags that say "stop"** in cancellation patterns.
- **Polling loops** where you must see the latest value (`while (!Volatile.Read(ref _stop)) ...`).

## When you need `Interlocked` instead

- **Read-modify-write** (counters, max, swap). `Volatile` doesn't give you atomicity, only ordering.
- **CAS-based publication** with retry (Treiber stack, lock-free queue heads).

## When you need `MemoryBarrier`

`Interlocked.MemoryBarrier()` (or `Thread.MemoryBarrier()`) inserts a *full* fence — no reads/writes on either side can cross it. Use cases are exotic; almost always `Volatile.Read/Write` is what you want. The full barrier shows up in:

- Implementing your own primitive that needs StoreLoad ordering (the strongest, missing from acquire/release).
- Interop with native code that has its own ordering rules.

## Common myth: "x86 is sequentially consistent so I don't need volatile"

Wrong. The CPU is *almost* SC for normal stores, but:

1. The JIT can still reorder reads/writes within a method.
2. ARM64 deployments (cloud, mobile, M1+) need the full memory model.
3. The reordering rules are subtle; programs that rely on x86's TSO are fragile.

**Always write to the .NET memory model**, not to a specific architecture's. `Volatile.Read/Write` is the line.

## Performance

`Volatile.Read` and `Volatile.Write` are *free* on x86 — they emit a normal load/store; the architecture provides the ordering. On ARM64 they emit `LDAR`/`STLR`, which costs slightly more than plain loads/stores but is still cheap (a few cycles).
