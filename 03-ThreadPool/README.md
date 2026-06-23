# 03 — The ThreadPool

> **Layer:** CLR runtime
> **Reading time:** ~30 minutes
> **Prereq:** [01](../01-Fundamentals/), [02](../02-OS-Threading-Model/)

The .NET ThreadPool is the engine. Every `Task.Run`, every `await` continuation (in the absence of a `SynchronizationContext`), every IO completion in async, every `ThreadPool.QueueUserWorkItem` — all of it runs there. If you don't understand its sizing decisions, starvation modes, and IO completion path, you cannot reason about *any* server performance issue.

## Architecture (post-.NET 6, the "managed pool")

Pre-.NET 6, the worker side of the pool was implemented in C++ inside CoreCLR. As of .NET 6 (with the `DOTNET_ThreadPool_UsePortableThreadPool=1` becoming default), it's a *fully managed* implementation in C#. This made it easier to reason about and improved consistency across platforms.

```
                  ┌─────────────────────────────────────────────┐
                  │                  ThreadPool                  │
                  │                                              │
                  │  ┌──────────────┐    ┌────────────────────┐  │
                  │  │  Workers     │    │  IO completion     │  │
                  │  │  (work-stealing │    (Windows IOCP /   │  │
                  │  │   deques)    │    │   Linux epoll      │  │
                  │  │              │    │   bridged thread)  │  │
                  │  └──────────────┘    └────────────────────┘  │
                  │                                              │
                  │  Hill-climbing controller                    │
                  │  (decides when to add/remove workers)        │
                  └─────────────────────────────────────────────┘
```

### Worker side

- **Per-worker local deque** + **global FIFO queue**. Work-stealing as in [01-Fundamentals/06-Work-Stealing](../01-Fundamentals/06-Work-Stealing/).
- Workers run user delegates and async continuations.
- Default minimum threads = `Environment.ProcessorCount`. Default max = a large number (32,767 on .NET 8). Configure via `ThreadPool.SetMinThreads` / `SetMaxThreads`.

### IO completion

- **Windows**: tied to IOCP. A small set of dedicated IO threads pull completion packets and post continuations to the worker pool.
- **Linux/macOS**: a managed pump (`SocketAsyncEngine`) on epoll/kqueue dispatches readiness notifications.

### Hill-climbing controller

This is the magic and the menace. The controller runs every ~500 ms and decides: should we add a worker thread, keep the same count, or let one retire?

Algorithm sketch:
1. Sample throughput (work items completed in the last interval).
2. Slightly perturb the worker count (Δ = ±1 or so).
3. If the perturbation correlated with throughput improvement, take a step in that direction.
4. Apply randomization to escape local optima.

Implication: **the pool reacts to load on a half-second timescale**. A burst of long synchronous work blocks workers, the controller eventually adds more, but for the burst's duration the pool is *starved*. This is the canonical [thread-pool starvation](Starvation/) failure mode.

## In-chapter folders

| Folder | Topic |
|---|---|
| [HillClimbing](HillClimbing/) | The throughput-controller algorithm and how to observe it |
| [Starvation](Starvation/) | What starvation looks like, how to repro, how to fix |
| [IOCP](IOCP/) | Windows IO Completion Ports and how async IO actually arrives |
| [WorkerThreads](WorkerThreads/) | The deque architecture, local vs global queues, stealing |
| [LongRunning](LongRunning/) | When to use `TaskCreationOptions.LongRunning` vs a dedicated `Thread` |
| [CustomSchedulers](CustomSchedulers/) | Writing a `TaskScheduler` for ordered/limited execution |

## Common knobs

| Setting | Where | Effect |
|---|---|---|
| `ThreadPool.SetMinThreads(workers, io)` | code | Pool will create up to `workers` workers immediately on demand, no hill-climb wait |
| `DOTNET_ThreadPool_ForceMinWorkerThreads` | env | Same, env-var version |
| `DOTNET_ThreadPool_ForceMaxWorkerThreads` | env | Hard cap |
| `DOTNET_ThreadPool_HillClimbing_*` | env | Tuning hill-climbing rate, gain, etc. |

For ASP.NET Core hosts, `Microsoft.AspNetCore.Hosting` doesn't tweak pool sizes for you. If you have a known concurrency profile, raising **min** is safer than raising **max** — it eliminates the controller-induced ramp without unleashing thousands of stuck threads.

## How to inspect

```bash
# Live thread counts and queue length
dotnet-counters monitor --process-id <pid> System.Runtime --counters threadpool-thread-count,threadpool-queue-length,threadpool-completed-items-count

# Detailed events: worker thread inject/retire, queue ops, hill-climbing decisions
dotnet-trace collect --process-id <pid> --providers Microsoft-Windows-DotNETRuntime:0x10000:5
```

`Microsoft-Windows-DotNETRuntime`'s `ThreadPoolWorkerThread*` events tell you exactly when the controller adds/removes workers and why.

## Anti-patterns covered later

- `Task.Run`-ing CPU-bound work and then blocking on it (`.Result`) → [18/SyncOverAsync](../18-Pitfalls-and-Anti-Patterns/SyncOverAsync).
- Long-running CPU work without `LongRunning` → starves the pool while hill-climbing reacts.
- IO that secretly blocks (`File.ReadAllText` from an async handler) → defeats the IOCP advantage.

## Run

```bash
dotnet run --project 03-ThreadPool
```
