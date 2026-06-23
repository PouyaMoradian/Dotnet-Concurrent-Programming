# MESI and its variants

**MESI** — the four-state cache-coherence protocol named for its states **M**odified / **E**xclusive / **S**hared / **I**nvalid — is the canonical cache coherence protocol, and the one whose state names you'll see in any architecture diagram. This file walks through the four states, the events that drive transitions, and the variants Intel (MESIF — adds a *Forwarder* state) and AMD (MOESI — adds an *Owned* state) use in practice.

You don't need to memorise the state diagram. You need to remember: **reads are cheap because lines can be Shared by many; writes are expensive because they require ownership**.

## The four states

Every cache line in every core's L1 (and L2) is in exactly one of these four states:

| State | Single sentence | Read locally? | Write locally? | Other cores can hold? |
|---|---|---|---|---|
| **M**odified | We have the only copy and it's dirty (differs from DRAM). | Yes | Yes | No |
| **E**xclusive | We have the only copy and it's clean (matches DRAM). | Yes | Yes (then → M) | No |
| **S**hared | Multiple cores have a clean copy. | Yes | No (must upgrade first) | Yes |
| **I**nvalid | The line is stale / not present. | No | No | (irrelevant) |

A few corollaries that are easy to forget:

- **E and M are equivalent for reads.** The only difference is whether the line matches DRAM. The CPU upgrades E → M silently when it writes.
- **S → M is the expensive transition.** That's the **RFO** (Read For Ownership). See [03-RFO-and-Contention.md](03-RFO-and-Contention.md).
- **Eviction from a writer's cache means writing back to DRAM** (or, with some variants, handing the line off to another cache).

## The events

The protocol responds to two kinds of events: a *local* core's load or store, and *remote* messages on the interconnect from other cores.

Local events:
- **Load** to address X: if the line is in M/E/S, hit; if I, fetch — possibly from another core.
- **Store** to address X: if the line is in M, write; if E, upgrade to M and write; if S, send RFO to invalidate other copies, upgrade to M, write; if I, send Read-For-Ownership (BusRdX), wait, write.

Remote messages we react to (`BusRd` = "another core is requesting a read of this line"; `BusRdX` = "another core is requesting exclusive access for a write"):
- **BusRd** (some other core wants to read): if we have M, flush our dirty value and downgrade to S; if E, downgrade to S; if S, no change.
- **BusRdX / Invalidate** (some other core wants to write): drop our copy → I.

The mechanical consequence: **the cost of a write scales with how many sharers had to be invalidated.** One sharer? One invalidate. 100 sharers (large server, hot read-only line)? 100 invalidates on the interconnect.

## State diagram, condensed

```
                  ┌──── store (mine) ────┐
                  │                      │
                  ▼                      │
         ┌──────────────┐                │
   (E) ──┘  Modified    ◄────────────────┘
   ▲ ▲    │   M         │                ▲
   │ │    └──┬───────────┘                │
   │ │       │  remote BusRd              │
   │ │       │     (flush + downgrade)    │
   │ │       ▼                            │
   │ │   ┌──────────────┐                │
   │ └───┤  Shared      ├────┐            │
   │     │   S          │    │  store     │
   │     └──┬───────────┘    │ (RFO)      │
   │        │                │            │
   │        │ remote         ▼            │
   │        │ Invalidate ┌──────┐         │
   │        ▼            │ M    │         │
   │     ┌──────┐        └──────┘         │
   │     │  I   │                          │
   │     └──┬───┘                          │
   │        │ local load                  │
   │        │   (BusRd: no sharers ⇒ E,   │
   │        │    sharers ⇒ S)             │
   │        ▼                              │
   │     ┌──────┐                          │
   └─────┤  E   │──────────────────────────┘
         └──────┘
```

(Read this as: from any state, on each event, the line moves along the labelled arrow. M/E/S all serve loads locally; only I needs to fetch.)

## MESIF — Intel's variant

Intel CPUs (since the QuickPath era) add a fifth state: **F**orward. When multiple caches hold a line in Shared, one of them is designated F. If a remote core then asks for the line, only the F-holder responds, instead of every S-holder racing to respond. This eliminates a class of redundant cache-to-cache transfers.

For software it's invisible — the contract is still MESI's. You just notice that on Intel, broad "fan-out reads" from a single producer scale better than the naive MESI back-of-envelope would predict.

## MOESI — AMD's variant

AMD adds **O**wned. The Owned state is "I have the only copy, and it's dirty, but other cores have it in Shared too". MESI requires that a dirty line have no sharers; transitioning M → S on a remote BusRd forces a writeback to DRAM. MOESI lets the line stay dirty *and* be shared, with one core in O serving reads. Net effect: fewer DRAM writes, more cache-to-cache transfers.

Again, invisible to software. AMD's MOESI is one reason hot-readers-with-a-few-writers patterns scale slightly better on Zen than on equivalent Intel chips.

## Snoop filters and directory protocols

On a single die with a small number of cores, every cache **snoops** every transaction on the interconnect — that's how it knows when to invalidate. This doesn't scale beyond ~16 cores; the interconnect bandwidth is finite.

Modern many-core CPUs use **directory-based coherence**: a directory (typically in the LLC) tracks which cores have each line. When core A wants to write, it queries the directory, which then issues targeted invalidates to just the sharers. Intel Sapphire Rapids uses this; AMD's Infinity Fabric uses a related design.

For software the implication is the same: writes still cost in proportion to sharers; the protocol just doesn't broadcast every transaction.

## What this means for .NET

- **Reads can share a line freely.** Many readers of an immutable struct field — no coherence traffic past the first fill.
- **Writers can't share a line freely.** Even one writer plus one reader on the same line means the line moves S↔M↔S on every store.
- **A `lock`'s footprint is the cache line holding `Monitor`'s internal state.** Each lock acquisition involves a **CAS** (Compare-And-Swap — read a value, compare it to an expected one, write a new value only if they match, all atomically) on that line. Two threads on the same lock = the line ping-pongs.
- **`Interlocked.Increment` is a CAS-style locked op.** Same story.

The architectural conclusion is the *patterns* file ([04-DotNet-Patterns.md](04-DotNet-Patterns.md)): when many threads need to write, don't write to one line. Shard, then aggregate.

## Practical takeaways

- M and E states "feel" the same to your code; the protocol only differentiates them to optimise eviction.
- S → M is the only state transition that involves coordinated multi-core traffic. Everything else is local-ish.
- MESIF/MOESI are optimisations that don't change the contract — don't write code that depends on which variant you're running on.

## Lab

There's no demo dedicated to *visualising* MESI states (we don't have access to coherence event counters from .NET). The behavioural consequence is in `ContendedInterlockedDemo` (demo 7) and `FalseSharingDemo` (demo 1) — both measure the cost of sustained S↔M transitions.

## Further reading

- **Wikipedia: MESI protocol** — the most accessible state diagram with worked transitions.
- **Hennessy & Patterson — *Computer Architecture: A Quantitative Approach*** — chapter on memory hierarchy and coherence, gold standard.
- **Sorin, Hill, Wood — *A Primer on Memory Consistency and Cache Coherence*** (free PDF, Synthesis Lectures) — short and complete.
