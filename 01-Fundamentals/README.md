# 01 — Fundamentals

> **Layer:** OS + CLR runtime
> **Reading time:** ~25 minutes
> **Prereq:** [00-Prerequisites](../00-Prerequisites/) — at minimum, you should know what false sharing is.

This chapter establishes the vocabulary the rest of the repo uses. Most concurrency confusion is *terminology* confusion. Get these distinctions right and the rest is mechanics.

## Five distinctions you must keep separate

| Pair | What they really mean |
|---|---|
| **Process vs Thread** | Process = isolated VM (own address space). Thread = scheduling unit *inside* a process. |
| **Concurrency vs Parallelism** | Concurrency = *structure* (independent activities). Parallelism = *execution* (literally simultaneous). You can have one without the other. |
| **Synchrony vs Asynchrony** | Sync = control returns when the work is done. Async = control returns immediately; completion is signalled later. |
| **Blocking vs Non-blocking** | Blocking = thread is parked while waiting. Non-blocking = thread keeps running. Async ≠ non-blocking unless the implementation cooperates. |
| **Mutable vs Immutable state** | Immutable state needs no synchronisation. Mutable state needs *all* of it. Most concurrency is the art of minimising the second. |

## Concurrency vs parallelism — the canonical example

```
Concurrency: one cook, three pots, alternates stirring.
Parallelism: three cooks, three pots, simultaneous.
```

A program is **concurrent** if it has multiple independent control flows in flight at once. It becomes **parallel** when those flows execute simultaneously on multiple cores. `async/await` on a single-threaded event loop (think Node.js, or .NET's UI thread before a `ConfigureAwait(false)`) is *concurrent but not parallel*. PLINQ is *parallel*. A web server with `Task.Run` is *both*.

## In-chapter sub-topics

| Folder | Topic |
|---|---|
| [Processes-vs-Threads](Processes-vs-Threads/) | Address spaces, isolation, cost of each, when to pick which |
| [Concurrency-vs-Parallelism](Concurrency-vs-Parallelism/) | Structure vs execution, with .NET examples |
| [Synchronization-vs-Asynchrony](Synchronization-vs-Asynchrony/) | What the keywords actually buy you |
| [State-and-Mutability](State-and-Mutability/) | Why immutability is the only free lunch in concurrency |
| [Memory-Visibility](Memory-Visibility/) | Why "set this flag" is not enough — preview of the memory model |
| [Work-Stealing](Work-Stealing/) | The thread pool's secret sauce, and how the TPL exploits it |

## A useful taxonomy of concurrent code

```
                    Is the work IO-bound or CPU-bound?
                              /          \
                           IO              CPU
                            |               |
              async / await + Channels    Parallel.* / PLINQ
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

Most "I have a concurrency bug" questions become tractable once the asker locates themselves on this tree.

## Run the labs

```bash
dotnet run --project 01-Fundamentals
```
