# Processes vs Threads

## Definitions you should be able to recite

| | Process | Thread |
|---|---|---|
| Address space | Own (isolated by MMU) | Shared with siblings |
| Crash blast radius | Confined | Whole process dies |
| File handles, sockets | Own | Shared |
| Default stack size | (process has heap) | 1 MB user-mode (Windows / Linux x64), 4 MB on Windows server. |
| Creation cost | ms range — `fork`/`CreateProcess` | µs range — `pthread_create`/`CreateThread` |
| Communication | IPC (pipes, sockets, shared memory, gRPC) | Shared memory + sync primitives |
| Scheduling unit | No (it's a container; threads are scheduled) | Yes |

## When to choose each

- **Threads** when you need shared memory, low coordination latency, and trust the code to not corrupt invariants. Most app concurrency.
- **Processes** when you need *isolation* — untrusted plugins, browser tabs, the JIT compiler service in some IDEs, multi-tenant work that must not leak.

## Threads in .NET specifically

A managed `System.Threading.Thread` is a thin wrapper around an OS thread. The runtime adds:

- A **managed thread ID** (`Environment.CurrentManagedThreadId`) distinct from the OS ID.
- **ExecutionContext / SynchronizationContext** flow.
- **Thread-static data** management.
- Hooks into the GC: managed threads enter "GC suspension" cooperatively at safepoints. A native thread doing P/Invoke must be re-entered cooperatively too.

`Thread` is rarely the right primitive in modern code. Reach for `Task` / `Task.Run` / `Parallel`. Use `new Thread` only when you genuinely need:

1. A thread that lives outside the pool (e.g., a high-priority dedicated audio loop).
2. A thread you want to set apartment state on (legacy COM interop).
3. A thread you want to keep busy without contributing to the pool's hill-climbing pressure (use `LongRunning` task instead in most cases — see [03/LongRunning](../../03-ThreadPool/LongRunning)).

## Cost comparison demo

The chapter's `ProcessVsThreadDemo` quantifies this on your machine. Approximate numbers on a 2024-vintage laptop:

| Action × 1000 | Wall time |
|---|---|
| `new Thread` start + join | ~150–400 ms |
| `Task.Run` (pool) | ~1–3 ms |
| Async no-op | ~0.1 ms |

A two-orders-of-magnitude gap. **The thread pool is not an optimisation; it's the right answer.**
