# The mechanism

To see why false sharing is so expensive, walk through what happens *in silicon*, byte by byte. The story is the cache-coherence protocol applied at the granularity of *lines*, not variables.

## The setup

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct Packed { public long A; public long B; }   // 16 bytes total, both on one line

static Packed _state;
```

`_state` is a 16-byte struct on the GC heap. Its `A` and `B` fields sit at offsets 0 and 8 of the same 64-byte cache line.

Thread 1 runs:
```csharp
for (int i = 0; i < N; i++) _state.A++;
```

Thread 2 runs concurrently:
```csharp
for (int i = 0; i < N; i++) _state.B++;
```

## Iteration 1 — establish ownership

Both threads start with the line in **I** (Invalid) in their L1.

Thread 1 issues `_state.A++` — a read-modify-write on offset 0:

1. **Load** `_state.A`: L1 miss; fetch line from L2/L3. Some other cache may have it in some state; let's say it's not yet anywhere, so the line lands in T1's L1 in **E** (Exclusive).
2. **Add 1**: arithmetic, no memory.
3. **Store** `_state.A`: T1's L1 line transitions E → M (Modified). The store retires into T1's store buffer; will eventually write to L1.

Concurrently T2 issues `_state.B++`:

1. **Load** `_state.B`: T2's L1 has the line in I. T2 sends a read request on the interconnect.
2. The coherence fabric routes this request. T1's cache snoops it, sees its own copy is M, *flushes* the dirty value to L2 (or directly forwards), and demotes its own line M → S (Shared).
3. T2 receives the data; its line is now in S.
4. T2 wants to write: line in S, needs M. Send **RFO** (Read For Ownership) to invalidate T1's copy.
5. T1 sees the invalidate; demotes S → I.
6. T2's line transitions S → M. Store retires.

Now T1 wants to increment `_state.A` again:

1. **Load**: T1's L1 has the line in I (just invalidated). Send read request.
2. T2 snoops; flushes dirty value; demotes M → S.
3. T1's line is now in S.
4. T1 wants to write: S → M, send RFO.
5. T2 demotes S → I.
6. T1's line is in M. Store retires.

And around and around. Every increment from either thread triggers a full RFO round-trip on the interconnect.

## What it costs

A modern interconnect round-trip:

| Distance | Latency |
|---|---|
| Adjacent cores, same mesh tile (Intel Sapphire Rapids) | ~30 ns |
| Across mesh (16-core part) | ~50 ns |
| Cross-CCD (AMD Genoa — CCD = Core Complex Die, AMD's chiplet) | ~80 ns |
| Cross-socket (UPI / IF — Intel Ultra Path Interconnect or AMD Infinity Fabric) | ~150 ns |

Per increment. So:

- Single-thread baseline: ~1 ns/increment (L1 hit, register, L1 write).
- Two threads false-sharing on a single die: ~30–50 ns/increment — **30–50× slower**.
- Two threads false-sharing across sockets: ~150 ns/increment — **150× slower**.

The cost is *purely* from the cache-coherence protocol; the actual work (1 add) is unchanged.

## Why "false"

At the C# level there's no shared state. `A` and `B` are different fields. The protocol's correctness reasoning ("if anyone has this line in M, downgrade it before someone else writes") is right — *given the line as a unit*. The protocol can't distinguish "T1 wrote bytes 0–7" from "T1 wrote bytes 0–15"; it sees "T1 has the line dirty".

There's no protocol-level fix because the protocol is doing what it must. The fix is *layout*: don't put them on the same line.

## Aside: store-to-load forwarding doesn't help cross-core

Within one core, the **store buffer** lets a later load see an earlier same-core store before it drains. So if T1 increments A and immediately reads B *on the same core*, it forwards from its store buffer.

Cross-core, there's no forwarding. T1 cannot peek at T2's store buffer. The only mechanism for the data to be visible to T2 is for T1's store to drain to L1 and for the coherence protocol to invalidate T2's view. That's the RFO loop.

## Why bigger lines (Apple Silicon, some POWER) make it worse

Apple Silicon uses **128-byte cache lines**. The good: a hot line holds twice the data, so prefetching pays off more. The bad: false sharing happens over 128 bytes of address space, so naive padding to 64 B doesn't fix it.

For portable code, pad to **128 B** to cover the worst case. The 64 wasted bytes are insignificant; the correctness across platforms is worth it.

## The "read-mostly" cousin — read-write false sharing

The above is *write-write* false sharing. There's also read-write:

- Thread 1 reads A repeatedly.
- Thread 2 writes B repeatedly.

What happens: when T2 writes, the line transitions to M in T2's cache; T1's S copy is invalidated. T1's next read brings the line back to S, demoting T2 to S. T2's next write does the dance again.

The traffic is the same — RFO on every T2 write, plus a remote read on every T1 read after that. The throughput cliff is roughly equivalent.

## The "padding kills it" intuition

Why does padding work? Because the protocol operates on lines. If `A` and `B` live on *different* lines, T1's writes to A's line never invalidate B's line. The lines can sit independently in their respective writers' L1s in M state forever, no traffic at all.

That's what `[StructLayout(LayoutKind.Explicit, Size = 64 or 128)]` buys you: enough wasted bytes that the next field lands on the next line.

## Practical takeaways

- The cost is the protocol doing its job at line granularity.
- It applies to write-write *and* read-write contention.
- The fix is layout — separate the contended fields onto separate lines.
- Apple Silicon's 128-byte lines justify padding to 128 by default in portable code.

## Lab

`FalseSharingDemo` (demo 1) measures packed vs padded. Run it and read the assembly with `DOTNET_JitDisasm` to confirm: the *machine code* is identical between the two variants — only the *struct layout* differs. The 5–10× speedup is purely from the cache being able to stay M-state on each writer's L1.

## Further reading

- **Paul McKenney — *Memory Barriers: a Hardware View*** — chapter on store buffers.
- **Joe Duffy — *Concurrent Programming on Windows*** — example walkthroughs in chapter 7.
- **Drepper — *What Every Programmer Should Know About Memory*** — false-sharing section.
