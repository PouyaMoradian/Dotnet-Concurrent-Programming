# State and Mutability — overview

> Concurrency bugs are bugs of *state*. There is no race on data that nobody mutates.

This is the single highest-leverage idea in concurrent programming, and it applies independently of language, runtime, or platform. Every other technique in this repo — locks, atomics, channels, immutable collections — is in service of managing mutable state correctly. The cheapest way to manage mutable state correctly is to not have any.

## The TL;DR

If you find yourself writing `lock(x) { x.Field = …; }`, ask first: can `x` be immutable? Can it be replaced wholesale instead of mutated in place? Can the field live in one task's exclusive memory (a "confined" state)? Each of those alternatives removes the need for the lock — and a lock you don't need is a lock that can't deadlock, can't starve writers, and can't surprise you under load.

## Read deeper

| File | What it covers |
|---|---|
| [01-Ladder-of-Safety.md](01-Ladder-of-Safety.md) | The six rungs from "no state" to "unsynchronised mutable state" |
| [02-Immutability-in-DotNet.md](02-Immutability-in-DotNet.md) | `record`, `ImmutableArray`, `Frozen*`, atomic publication patterns |
| [03-Confinement-Patterns.md](03-Confinement-Patterns.md) | Single-writer designs, actors, `ThreadLocal`, channel-as-mailbox |

## The interview question

> "What's the difference between thread safety and immutability?"

Immutability is a *property of the type*. Thread safety is a *property of how the type is used*. An immutable type is thread-safe trivially. A mutable type can be thread-safe (`ConcurrentDictionary`) or not (`Dictionary`). Most production bugs are mutable types being used as if they were thread-safe.

## The demo

`MutableStateRace` runs eight threads each incrementing a shared `int` 1,000,000 times. Expected total: 8,000,000. The unguarded version is *systematically* wrong because `i++` is a three-instruction sequence (load, increment, store), not an atomic operation. You'll see the unguarded version lose tens or hundreds of thousands of updates per run. The `Interlocked` and `lock` versions both produce the correct total, with different performance profiles.

A subtler demo, `TornLongReadDemo`, shows the worse symptom: a 64-bit field can be read mid-write and produce a value that *was never written*. On 32-bit architectures this is routine; on 64-bit it's rare but still possible if the field is misaligned.

Run them and notice: the race always *loses* updates; it never gains them. That asymmetry is the signature of write-after-write contention without ordering.
