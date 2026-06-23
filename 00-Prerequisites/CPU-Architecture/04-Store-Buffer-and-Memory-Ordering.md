# The store buffer and memory ordering

Memory ordering is one of those subjects that has been ruined by being introduced with formal models first. The model is best understood by starting at the bottom: a piece of silicon called the **store buffer**, and what it does to make x86 "almost sequential" and ARM "definitely not".

This file is the *preview*. Chapter 12 is the full treatment with the CLR memory model and C# tools. Read this one once, then refer back to it when you encounter `Volatile.Read`, `Volatile.Write`, `Interlocked`, or `Thread.MemoryBarrier` later.

## What the store buffer is

When a core executes a store instruction, the architectural definition says "memory at address X now contains value V". The implementation, however, doesn't *immediately* write V into L1d. Instead it pushes `(X, V)` into a per-core FIFO called the **store buffer**, and considers the store retired. The actual L1 write happens later — maybe a few cycles, maybe a few hundred — when the buffer drains.

Why? Two reasons:

1. **Coherence.** Writing to a cache line means owning it (M — Modified — state in the **MESI** protocol: Modified / Exclusive / Shared / Invalid). If the line isn't owned, you have to send an **RFO** (Read For Ownership) message and wait. The store buffer lets the core retire the store now and *queue* the RFO, never blocking execution.
2. **Pipelining.** Each cycle, the core wants to retire 3–6 µops. Waiting for L1 — never mind another socket — would torpedo throughput.

```
         core 0                                 core 1
   ┌─────────────────┐                    ┌─────────────────┐
   │ pipeline issues │                    │ pipeline issues │
   │ store X=1       │                    │ load Y          │
   └────────┬────────┘                    └────────┬────────┘
            │                                      │
            ▼                                      ▼
   ┌─────────────────┐                    ┌─────────────────┐
   │ store buffer    │                    │ load buffer     │
   │   (X=1) ←──────┐│                    │                 │
   └────────┬───────│┘                    └────────┬────────┘
            │       │                              │
            ▼       │  store-to-load forward       ▼
         L1d        │  (same core only)            L1d
            │       │                              │
            └───────┘────────── interconnect ──────┘
```

## Store-to-load forwarding: same core only

Loads on the *same core* can **forward** from the store buffer. So this code, on one thread, always works:

```csharp
_a = 1;
int x = _a;   // x is observed as 1 by this same thread, even if the store hasn't drained
```

The CPU forwards the value from the store buffer back into the pipeline.

But another core cannot peek into your store buffer. Until your store drains to L1 *and* the coherence protocol propagates the new ownership, other cores see the *old* value.

This is the single mechanical reason x86 allows the famous "store-load reordering":

```
Thread A:                    Thread B:
  X = 1;                       Y = 1;
  r1 = Y;                      r2 = X;
```

On x86, after both threads run, `(r1, r2) == (0, 0)` is a *legal* outcome. Why? Both stores can be sitting in their respective store buffers when the loads execute. Each thread's load sees memory that's still pre-store from the other thread's perspective.

## The memory models, in one line each

| Model | Allows | Used by |
|---|---|---|
| **Sequential consistency** (SC) | No reorderings | A textbook ideal; no real CPU |
| **Total Store Order** (TSO) | Store→Load only | x86, x86-64 |
| **Processor Consistency** (PC) | Store→Load + per-thread stores can be reordered as observed by others (with constraints) | Largely historical (Goodman's model; early x86 before TSO was formalised) |
| **Release Consistency** (RC) | Everything except where you put an explicit barrier | ARM64, POWER, RISC-V |
| **Relaxed** | All reorderings | A theorist's model; no real CPU is fully relaxed for atomics |

What matters for .NET:

- **x86 TSO is forgiving.** Most race-free or "almost race-free" code works. The store-load reorder is the gotcha (it's the heart of Dekker's algorithm corner cases).
- **ARM64 is unforgiving.** Without barriers, a store and a load from different addresses on the same thread can be observed *out of program order* by another thread.

## .NET's guarantees

The CLR memory model is documented in the ECMA-335 standard and the .NET runtime's design docs. The key promises in plain English:

1. **Reads and writes of `int`, `long` (aligned), `bool`, references, and other primitives up to native word size are atomic.** A reader will never see a half-written value… unless the field is misaligned. Demo `TornLongReadDemo` in chapter 1 shows a torn read on a misaligned 64-bit field on a 32-bit-friendly process.
2. **`volatile` fields** have **acquire** semantics on read and **release** semantics on write. *No* later read may be reordered before an acquire; *no* earlier write may be reordered after a release.
3. **`Interlocked` operations are full fences.** Every `Interlocked.*` call orders all reads and writes before it with respect to all reads and writes after it, on the calling thread.
4. **`Thread.MemoryBarrier()`** is a full fence. Equivalent to `mfence` on x86 or `dmb ish` on ARM.

What you should write today, for 99% of cases:

| You want to… | Use |
|---|---|
| Read a flag set by another thread once at startup | `Volatile.Read(ref _flag)` |
| Publish a constructed object to another thread | Store with `Volatile.Write` after the constructor, read with `Volatile.Read` |
| Compare-and-swap | `Interlocked.CompareExchange` |
| Increment a counter (rarely contended) | `Interlocked.Increment` |
| Increment a counter (often contended) | Per-thread sharded; sum at read; see [Cache-Coherency/04-DotNet-Patterns.md](../Cache-Coherency/04-DotNet-Patterns.md) |

## The "wait, what's the bug?" example

The classic *check-then-act* race:

```csharp
class Lazy<T> where T : class
{
    private T? _value;
    private readonly Func<T> _factory;
    public Lazy(Func<T> factory) { _factory = factory; }
    public T Value
    {
        get
        {
            if (_value == null)        // ① check
                _value = _factory();   // ② act
            return _value;
        }
    }
}
```

Two threads call `Value` concurrently. Race 1: both see `_value == null` and call the factory twice — possibly non-idempotent. Race 2 (memory model): thread A constructs the object and writes `_value`, but the *fields* of the object haven't drained to memory yet. Thread B sees `_value != null`, returns it, dereferences a field, and reads a zero. On x86 the second race is rare-to-impossible for fully-constructed objects (TSO orders stores), but on ARM64 it's a real, observable bug.

The fixes:

```csharp
// 1. Use the BCL: Lazy<T> handles all of this.
private readonly Lazy<MyService> _svc = new(() => new MyService());

// 2. Or, hand-rolled with release semantics:
private MyService? _svc;
public MyService Get()
{
    var local = Volatile.Read(ref _svc);
    if (local != null) return local;
    var fresh = new MyService();
    var prior = Interlocked.CompareExchange(ref _svc, fresh, null);
    return prior ?? fresh;
}
```

The `Volatile.Read` ensures that *if* we see `_svc != null`, all the writes that built `_svc` happened-before our read. The `Interlocked.CompareExchange` ensures only one thread's `fresh` wins.

## Practical takeaways

- **Hardware reorders. The JIT reorders. Live with it.**
- **`lock` makes the problem disappear** for the duration of the critical section, at the cost of contention. Inside the lock the memory model is sequential.
- **For wait-free flag passing, `Volatile.Read`/`Write` is the cheapest correct tool.**
- **For atomic state transitions, `Interlocked.*`.**
- **You almost never need `Thread.MemoryBarrier()` explicitly.** If you reach for it, you're probably doing something Chapter 12 has a better answer for.

## Lab

The torn-read demo lives in chapter 1 (`TornLongReadDemo`). The "publisher" pattern (release-write, acquire-read) lives in `MemoryVisibilityDemo` in chapter 1. We deliberately keep those *behavioural* demos in chapter 1; this section is the *mechanical explanation* of why they behave the way they do.

## Further reading

- **Hans Boehm — *Threads cannot be implemented as a library*** (foundational paper).
- **Paul McKenney — *Memory Barriers: a Hardware View for Software Hackers*** (free PDF; goes to the silicon).
- **Jeff Preshing — `preshing.com`** (best on-the-internet tour for programmers).
- **Sergey Tepliakov — *Memory Model in .NET*** (deep dive into the CLR specifics).
- **ECMA-335 §I.12.6** — the spec itself for the CLI memory model.
