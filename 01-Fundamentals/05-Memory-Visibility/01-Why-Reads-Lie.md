# Why reads "lie" — the physical picture

Three actors are between your source code and the memory bus, each independently free to reorder operations as long as the *single-threaded* behaviour of the program is preserved:

1. **The compiler / JIT.** It can hoist a load out of a loop, reuse a value in a register, dead-store-eliminate a write, and reorder independent statements.
2. **The CPU's store buffer.** Modern x86 cores hold pending writes in a buffer that drains to L1 cache "soon" — but not immediately, and not necessarily in program order on weaker models.
3. **The CPU's out-of-order execution engine.** Modern CPUs execute several hundred instructions speculatively past the current instruction pointer. The retired-and-committed order is *eventually* in program order, but the visible-to-other-cores order is whatever the cache coherence protocol enforces.

Together they explain why "thread A wrote `data = 42` before `ready = true`" doesn't imply "thread B observing `ready == true` will see `data == 42`."

## What each actor is allowed to do

### The JIT

```csharp
// Source
while (!_done) DoWork();
```

The JIT looks at this and notices that `DoWork()` doesn't read `_done`. So it can lift the load:

```csharp
// JIT-optimised
var done = _done;
while (!done) DoWork();   // <-- infinite loop if another thread sets _done = true
```

This is *legal* under the JIT's single-thread reasoning. It only becomes a bug because `_done` is shared. The fix is `Volatile.Read(ref _done)` (which the JIT must reload every iteration) or the `volatile` keyword on the field.

### The store buffer

```
Core 0:
  ST data, 42        ────→  [store buffer slot 1]  ────→  L1 cache
  ST ready, 1        ────→  [store buffer slot 2]
                                                   ↑
                              drains in slot order on x86,
                              free order on ARM/Power
```

On x86 the store buffer drains in FIFO order, which gives x86 its **Total Store Order (TSO)** memory model — your stores become visible to other cores in program order. (Loads from different addresses can still appear to other cores out of order with respect to stores; that's why even x86 needs `MFENCE` for some patterns.)

On ARM64 the store buffer can drain in *any* order. So thread B can see `ready = 1` before `data = 42`, even though thread A wrote `data` first.

### The out-of-order execution engine

A CPU pipeline is hundreds of stages deep. When the front-end fetches instructions, several may run "in parallel" inside the core via multiple execution units. The CPU tracks dependencies and ensures the *result* matches in-order execution from the program's point of view — but only with respect to *this* core. To another core watching memory, the order can be anything.

Practical example: a store to `data` followed by a load from `ready` can be reordered so the load runs before the store retires. This is called **store-load reordering** and is allowed even on x86's TSO model. It's why Dekker's algorithm doesn't work without barriers.

## The hierarchy of fixes

If you can't have any reordering between two specific memory operations, you need a barrier:

| Barrier | What it blocks | Cost |
|---|---|---|
| Acquire (load-fence) | Subsequent loads can't be reordered before it | Cheap |
| Release (store-fence) | Prior stores can't be reordered after it | Cheap |
| Full barrier | Everything before, everything after | Expensive (drains store buffer) |
| Lock (Monitor) | Acquire on enter, release on exit | Cheap when uncontended; expensive when contended |

In .NET:

- `Volatile.Read(ref x)` is an **acquire load**.
- `Volatile.Write(ref x, v)` is a **release store**.
- `Interlocked.*` operations are **full barriers** on most operations (some, like `Interlocked.Read`, are formally acquire-only).
- `lock(obj) { … }` is acquire on entry, release on exit.
- `Thread.MemoryBarrier()` is a full barrier.

You almost never reach for `Thread.MemoryBarrier` directly in application code. You reach for `Volatile.Read/Write` for single-variable flags, `Interlocked.*` for atomic updates, and `lock` for everything else.

## What's an "atomic" write?

A write is *atomic* if no observer ever sees a half-completed value. On 64-bit .NET:

- Reference assignment is atomic (the runtime aligns references to pointer size).
- `int`, `bool`, `float`, `short`, `byte` are atomic (≤ pointer size).
- `long`, `double` are atomic **if aligned** to 8 bytes. The default layout aligns them, but explicit `[StructLayout]` packs or unaligned array indexing can break this.

So a 64-bit field that happens to straddle two cache lines (or two 4-byte words on a 32-bit runtime) can produce a "torn" read: the reader gets the high half of the new value and the low half of the old value, an aggregate that was never written. `TornLongReadDemo` shows this.

The defence:

```csharp
// Portable, allocation-free atomic long read.
long v = Interlocked.Read(ref _longField);
```

`Interlocked.Read` is guaranteed atomic regardless of alignment or architecture.

## The takeaway

There are four classes of memory bug:

1. **Lost updates** — `i++` from multiple threads. Fix: `Interlocked.Increment` or `lock`.
2. **Stale reads** — reader keeps a register copy of a value the writer has updated. Fix: `Volatile.Read`.
3. **Torn reads** — reader sees a value mid-write. Fix: `Interlocked.Read` (longs) or `lock`.
4. **Reordered observations** — reader sees writes in a different order than the writer made them. Fix: `Volatile.Write`/`Volatile.Read` pair, or `lock` on both sides.

A `lock` fixes all four because it implies a full memory barrier on both entry and exit. The lock-free primitives are cheaper, with finer-grained guarantees — pay only for what you need.
