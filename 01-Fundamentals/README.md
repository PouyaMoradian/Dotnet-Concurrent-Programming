# 01 — Fundamentals

> **Layer:** OS + CLR runtime
> **Reading time:** ~90 minutes (full chapter), ~25 minutes (this index)
> **Prereq:** [00-Prerequisites](../00-Prerequisites/) — at minimum, you should know what false sharing is.

This chapter establishes the vocabulary, mental models and physical intuitions the rest of the repo relies on. Most concurrency confusion is *terminology* confusion compounded by *physical* misconceptions about what the hardware is actually doing. Get these distinctions right and the rest is mechanics.

Each subtopic below is split into a short overview (the folder's `README.md`) plus three or four deep-dive notes. Read the index first, then pick the deep-dives you need. The chapter is meant to be the lens you'll use to read everything later — invest time here and the higher chapters will feel like inevitabilities rather than surprises.

---

## Why a fundamentals chapter at all?

Concurrent programming is not a feature you bolt onto sequential programming; it's a different *physics*. In sequential code there is one clock, one notion of "what happened before what", and one observer (your debugger). The instant you have two threads, you have:

- **Two clocks** that aren't synchronised at the nanosecond level.
- **Multiple observers** (each CPU core has its own view of memory until caches reconcile).
- **A schedule you don't control** (the OS, the .NET ThreadPool, and the JIT can each reorder things).
- **A failure mode that's invisible in the source code** — the program may work in your tests and break on a production machine with a different memory model.

Almost every bug in a concurrent program is a failure to internalise one of those four facts.

---

## Five distinctions you must keep separate

If you only remember one diagram from this chapter, make it this one:

| Pair | What they really mean | Mistake people make |
|---|---|---|
| **Process vs Thread** | Process = isolated VM (own address space). Thread = scheduling unit *inside* a process. | Thinking "thread" means "any kind of background work". |
| **Concurrency vs Parallelism** | Concurrency = *structure* (independent activities). Parallelism = *execution* (literally simultaneous). | Calling `Parallel.ForEach` on IO-bound work because "we want it concurrent". |
| **Synchrony vs Asynchrony** | Sync = control returns when the work is done. Async = control returns immediately; completion is signalled later. | Believing `async` automatically means "faster" or "non-blocking". |
| **Blocking vs Non-blocking** | Blocking = thread is parked while waiting. Non-blocking = thread keeps running. | Treating async as a synonym for non-blocking. |
| **Mutable vs Immutable state** | Immutable state needs no synchronisation. Mutable state needs *all* of it. | Adding a lock instead of removing the shared state. |

These five are nearly orthogonal. You can be:

- Sync, concurrent, parallel, with mutable state (most desktop apps with worker threads).
- Async, concurrent, *not* parallel, with confined state (`async/await` on a single-threaded UI loop).
- Sync, single-threaded, *not* concurrent (the simplest program you can write).
- Async, concurrent, parallel, with immutable state (a healthy ASP.NET Core handler).

The last row is the goal of most modern server design.

---

## Concurrency vs parallelism — the canonical example

```
Concurrency: one cook, three pots, alternates stirring.
Parallelism: three cooks, three pots, simultaneous.
```

Both feed three pots faster than one-at-a-time. But:

- The single cook only helps if some of the stirring time overlaps with *waiting* (the water heating, the pasta cooking). That's IO. Concurrency hides latency.
- The three cooks help even when there is nothing to wait for — they grind work in parallel. That's CPU. Parallelism shortens compute.

A program is **concurrent** if it has multiple independent control flows in flight at once. It becomes **parallel** when those flows execute simultaneously on multiple cores. `async/await` on a single-threaded event loop (think Node.js, or .NET's UI thread before a `ConfigureAwait(false)`) is *concurrent but not parallel*. PLINQ is *parallel*. A web server with `Task.Run` is *both*.

See [02-Concurrency-vs-Parallelism/](02-Concurrency-vs-Parallelism/) for the formal definitions, more examples, and the matching .NET tooling.

---

## A useful taxonomy of concurrent code

When someone asks you to "make this concurrent", the first job is to locate the work on this tree:

```
                    Is the work IO-bound or CPU-bound?
                              /          \
                           IO              CPU
                            |               |
              async / await + Channels    Parallel.* / PLINQ / SIMD
                            |               |
              shared state? ←──────────────→ shared state?
                  /     \                       /     \
              none      yes                  none     yes
                |         |                    |       |
            no sync    pick a sync         no sync   pick a
            needed     primitive (04)      needed    sync prim (04)
                                                       or atomic (05)
                                                       or immutable (06)
```

Most "I have a concurrency bug" questions become tractable once the asker locates themselves on this tree. The corollary is that most "I have the wrong tool" questions trace back to a misclassification: the work was IO-bound but someone reached for `Parallel.ForEach`, or it was CPU-bound but someone wrapped it in `async`.

---

## The chapter map

| Folder | Topic | Files inside |
|---|---|---|
| [01-Processes-vs-Threads/](01-Processes-vs-Threads/) | Address spaces, isolation, cost of each, when to pick which | overview + address spaces + threads in depth + .NET threads + cost comparison |
| [02-Concurrency-vs-Parallelism/](02-Concurrency-vs-Parallelism/) | Structure vs execution, with .NET examples | overview + definitions + patterns + .NET tooling |
| [03-Synchronization-vs-Asynchrony/](03-Synchronization-vs-Asynchrony/) | What the keywords actually buy you | overview + four quadrants + async mechanics + common confusions |
| [04-State-and-Mutability/](04-State-and-Mutability/) | Why immutability is the only free lunch in concurrency | overview + ladder of safety + immutability in .NET + confinement patterns |
| [05-Memory-Visibility/](05-Memory-Visibility/) | Why "set this flag" is not enough — preview of the memory model | overview + why reads lie + memory models + .NET tools |
| [06-Work-Stealing/](06-Work-Stealing/) | The thread pool's secret sauce, and how the TPL exploits it | overview + deques and stealing + threadpool internals + implications |

Each folder's `README.md` is a short table of contents pointing at the deep-dive notes. The deep-dives are where the actual depth lives — diagrams, derivations, citations, edge cases.

---

## The labs in this chapter

The executable is `Chapter01.Fundamentals` and exposes the following demos:

| # | Demo | What it shows |
|---|---|---|
| 0 | `ProcessVsThreadDemo` | Threads vs Tasks vs async no-op — three orders of magnitude apart |
| 1 | `ConcurrencyVsParallelismDemo` | `await` hides latency; `Parallel.Invoke` shortens compute |
| 2 | `SyncVsAsyncDemo` | 200 fake "requests" — async finishes in a fraction of the threads |
| 3 | `MutableStateRace` | The textbook race: `i++` under contention loses updates |
| 4 | `MemoryVisibilityDemo` | Producer/consumer with release-acquire via `Volatile.Read/Write` |
| 5 | `WorkStealingDemo` | One producer, many consumers — the pool stays busy |
| 6 | `TornLongReadDemo` | A 64-bit field read concurrently with writes can produce a value that was never written |
| 7 | `FalseSharingDemo` | Two unrelated counters on the same cache line cost ~5–10× more than padded ones |
| 8 | `DeadlockDemo` | Two locks acquired in opposite orders deadlock; `Monitor.TryEnter` with a timeout escapes |
| 9 | `ThreadHoppingDemo` | An `async` method can run on a different thread on every line — a visualisation |

Run the picker:

```bash
dotnet run --project 01-Fundamentals
```

Or run a single demo by index:

```bash
dotnet run --project 01-Fundamentals -- 7   # false sharing
```

Or pipe `a` to the picker for "all":

```bash
echo a | dotnet run --project 01-Fundamentals
```

---

## How to read this chapter

If you have an hour, do this in order:

1. **Skim each folder's `README.md`** (5 min each = 30 min).
2. **Run the demos**, picking `a` to run all of them (10–15 min).
3. **Pick the two deep-dives you found least intuitive** in step 1 and read them (15 min).
4. **Bookmark this chapter** — you'll come back here whenever something in chapters 02–18 surprises you.

If you only have ten minutes, read [02-Concurrency-vs-Parallelism/01-Definitions.md](02-Concurrency-vs-Parallelism/01-Definitions.md) and [04-State-and-Mutability/01-Ladder-of-Safety.md](04-State-and-Mutability/01-Ladder-of-Safety.md). Those two between them cover ~70% of the conceptual errors I see in production code.

---

## Glossary (used throughout the repo)

- **Worker thread** — a thread the ThreadPool uses to run user CPU work. There are also IO completion threads on Windows; on Linux the distinction is mostly an implementation detail of the runtime.
- **Synchronisation context** — an object that captures "where to resume" after an `await`. UI frameworks set one (the UI thread); ASP.NET Core does not.
- **Race condition** — a bug where the correctness of the program depends on which thread happens to run first.
- **Data race** — a strictly stronger condition: two threads access the same location, at least one writes, and there is no ordering primitive between them. Most data races are race conditions; many race conditions are not data races (they're logical races, e.g. check-then-act).
- **Memory barrier** — an instruction that constrains the order in which loads and stores become visible to other cores.
- **Happens-before** — a partial order relating program events such that, if `A` happens-before `B`, every effect of `A` is visible to `B`.
- **Lock-free** / **wait-free** — properties of algorithms that progress without locks (lock-free: *some* thread makes progress) or without any thread ever waiting (wait-free: *every* thread makes progress in bounded steps).

You'll see each of these defined in the chapter where they first earn their keep. This glossary is the cheat sheet.
