# Cache coherency

Multiple cores, each with their own L1 and L2, **must** see a consistent view of memory — that is the single hardware promise that makes `Interlocked.CompareExchange` meaningful. The mechanism that delivers it is the *cache coherence protocol*.

## MESI in 90 seconds

Every cache line, in every core's L1, is in one of four states:

| State | Meaning | Read by us? | Write by us? | Other cores can hold? |
|---|---|---|---|---|
| **M**odified | We have the only copy and it's dirty | yes | yes | no |
| **E**xclusive | We have the only copy and it's clean (matches DRAM) | yes | yes (then → M) | no |
| **S**hared | Multiple cores have a clean copy | yes | no (must upgrade) | yes |
| **I**nvalid | The line is stale | no | no | irrelevant |

Variants you may meet: **MESIF** (Intel; F = Forwarder, picks one cache to satisfy reads), **MOESI** (AMD; O = Owned, lets a dirty line stay shared without writing through to memory).

## The expensive transition: S → M

When core A wants to write to a line that's currently in **S** in cores B and C:

1. A sends an **RFO** (Read For Ownership) message on the interconnect.
2. B and C invalidate their copies (transition I).
3. A's line transitions S → M.
4. A's write retires from the store buffer to L1.

That round-trip is the price of contention. On a modern Xeon with mesh interconnect, an RFO is ~30–50 ns. On a 96-core EPYC across CCDs, it can be 100+ ns. **All** locked operations (`Interlocked`, `lock` cmpxchg) include this when contended.

## What the .NET developer should take from this

- **Reads are nearly free across cores.** Many readers can share a line in **S**. Read-heavy data scales.
- **Writes are expensive in proportion to contention.** One writer? Cheap. Many writers to one line? Pathological.
- **Therefore: separate writer state.** Per-thread counters with a final aggregate. Sharded `ConcurrentDictionary`. Striped locks. The `ThreadStatic`/`ThreadLocal` pattern. `Parallel.For` with the `localInit`/`localFinally` overload.

## Demonstration

The `FalseSharingDemo` in this chapter shows the pathological case in vivid colour: two threads, two unrelated counters, but on the same cache line. The "padded" version places them on separate lines and runs ~5–10× faster. *Same code, same threads, same number of writes — only the layout changed.*

## Further reading

- Paul McKenney — *Memory Barriers: a Hardware View for Software Hackers* (free PDF; the canonical "what's actually in the silicon" doc).
- Intel SDM Vol. 3A, "Memory Ordering".
- Jeff Preshing's blog (preshing.com) — best in-depth tour aimed at programmers.
