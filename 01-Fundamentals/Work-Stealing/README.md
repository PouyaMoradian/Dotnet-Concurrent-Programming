# Work stealing

Work stealing is the technique that lets the .NET ThreadPool keep cores busy when one of them runs out of local work. It's the secret sauce behind why `Task.Run` is so cheap and why the TPL scales.

## The data structures

Each ThreadPool worker has:

- A **local deque** (double-ended queue). LIFO push & pop on the *front*; FIFO steal from the *back*.
- A reference to the shared **global queue** (FIFO). Used when work has no preferred worker.

When a task is queued by code running on a worker thread, it lands in *that worker's* local deque (front). When code outside the pool queues a task (e.g., from `Main` before any pool work has run), it lands on the global queue.

```
                       ┌────────── global queue (FIFO) ──────────┐
                       └────────────┬───┬───┬───┬─────────────────┘
                                    ↓
   Worker 0:  [ T9 ← T8 ← T7 ← T6 ← T5 ← T4 ← T3 ← T2 ← T1 ]   ← LIFO own pop
                                                          ↑    ← FIFO foreign steal
   Worker 1:  [ ... ]
   Worker 2:  [ empty → goes stealing ]
```

## Why this layout?

- **LIFO local pop** maximises cache locality: the task you just queued is hot in your cache; pop it next.
- **FIFO foreign steal** minimises contention: the *thief* takes from the *bottom* — maximally far from where the victim is working. This means almost-no cache-line ping pong on the deque metadata.
- **Global queue as overflow / pre-pool source.**

This is the Cilk / Java ForkJoinPool design, ported to .NET. In .NET 6+ the implementation includes additional tweaks (separate IO completion path on Windows IOCP; on Linux, an epoll-based event loop in CoreCLR for sockets; thread-injection via hill-climbing — see [03-ThreadPool](../../03-ThreadPool/)).

## What the developer should take from this

1. **Spawning many tasks from a worker is fine.** They land local, run hot.
2. **Spawning from Main / a non-pool thread** lands on the global queue and may take a hop to be noticed. Trivial in scale, not free.
3. **Don't structure code that depends on FIFO ordering across cores.** The pool is allowed to reorder; ordered processing belongs in `Channel<T>` (single reader) or in your own pipeline.

## See also

- [03-ThreadPool](../../03-ThreadPool/) — full deep-dive: hill-climbing, IO completion threads, worker injection, starvation, custom schedulers.
- The `WorkStealingDemo` in this chapter is a tiny sanity check; the *real* picture comes from `dotnet-counters monitor System.Runtime threadpool-thread-count threadpool-queue-length` while running the demo.
