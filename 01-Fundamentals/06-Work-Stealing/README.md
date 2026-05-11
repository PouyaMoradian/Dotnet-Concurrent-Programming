# Work stealing — overview

Work stealing is the technique that lets the .NET ThreadPool keep cores busy when one of them runs out of local work. It's the secret sauce behind why `Task.Run` is so cheap and why the TPL scales.

Without it, you'd need a single global queue protected by a lock, and every pool thread would contend on it — the canonical "scaling cliff" of naive thread pools. With it, each worker has its own queue and only goes looking for work from a peer when its own is empty, so contention is rare and locality is high.

This chapter introduces the *what* and the *why*. The full implementation details — hill-climbing, IO completion ports, custom schedulers — live in [03-ThreadPool](../../03-ThreadPool/).

## Read deeper

| File | What it covers |
|---|---|
| [01-Deques-And-Stealing.md](01-Deques-And-Stealing.md) | The data structures (local deque + global queue), the LIFO-own / FIFO-foreign split, why it preserves cache locality |
| [02-DotNet-ThreadPool-Internals.md](02-DotNet-ThreadPool-Internals.md) | How the .NET ThreadPool wires up workers, IO completion, hill-climbing, and the global queue |
| [03-Implications-For-Code.md](03-Implications-For-Code.md) | What this means when you write `Task.Run`, `Parallel.For`, and `Channel<T>` consumers |

## The 30-second version

Each ThreadPool worker has:

- A **local deque** (double-ended queue). LIFO push & pop on the *front*; FIFO steal from the *back*.
- A reference to the shared **global queue** (FIFO). Used when work has no preferred worker.

```
                       ┌────────── global queue (FIFO) ──────────┐
                       └────────────┬───┬───┬───┬─────────────────┘
                                    ↓
   Worker 0:  [ T9 ← T8 ← T7 ← T6 ← T5 ← T4 ← T3 ← T2 ← T1 ]   ← LIFO own pop
                                                          ↑    ← FIFO foreign steal
   Worker 1:  [ ... ]
   Worker 2:  [ empty → goes stealing ]
```

When code running *on* a worker queues a task, it lands in that worker's local deque. When code *outside* the pool queues a task (e.g., from `Main` before any pool work has run), it lands in the global queue.

The asymmetry of LIFO-own / FIFO-foreign is the whole trick. LIFO-own keeps the worker hot on its most recent work (cache locality). FIFO-foreign means the thief grabs from the bottom of the victim's deque — maximally far from where the victim is working, so the victim doesn't pay a cache-line ping-pong on the deque metadata.

## See also

- [03-ThreadPool](../../03-ThreadPool/) — full deep-dive: hill-climbing, IO completion threads, worker injection, starvation, custom schedulers.
- The `WorkStealingDemo` in this chapter is a tiny sanity check; the *real* picture comes from `dotnet-counters monitor System.Runtime threadpool-thread-count threadpool-queue-length` while running the demo.
