# ThreadPool starvation

Already covered deeply in [03/Starvation](../../03-ThreadPool/Starvation/). The signature symptom: latency P99 climbs while CPU usage is *low*. Workers are alive but blocked.

## The five most common causes (production)

1. **Sync-over-async**: `.Result` / `.Wait()` in a code path that runs on a worker.
2. **Blocking IO disguised as async** (sync FileStream, ADO.NET sync APIs in an async method).
3. **CPU-bound work scheduled on the pool while async work is also pending** — the pool fills with CPU loops; async continuations queue up.
4. **Long-running tasks without `LongRunning`** — a never-completing task occupies a worker indefinitely.
5. **Min threads set too low for cgroup-limited containers** — default `Min = ProcessorCount` may be 1 or 2 in a small container; first burst of concurrency stalls until hill-climbing reacts.

## How to detect it

- `dotnet-counters monitor System.Runtime --counters threadpool-thread-count,threadpool-queue-length,threadpool-completed-items-count`
- Watch `threadpool-thread-count` climb steadily.
- Watch `threadpool-queue-length` grow.
- Wall-time stacks in PerfView showing many threads in `Wait*` / `SemaphoreSlim.Wait*`.

## How to fix

| Cause | Fix |
|---|---|
| Sync-over-async | Async-end-to-end |
| Blocking IO | Use `*Async` overloads with `useAsync: true` streams |
| CPU on pool | Move CPU to a separate scheduler or use `LongRunning` |
| Long tasks | `Task.Factory.StartNew(..., LongRunning)` or `new Thread` |
| Min threads | `ThreadPool.SetMinThreads(workers, io)` at startup |

## Prevention strategy

1. **Audit your code** for `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`. Almost all are bugs.
2. **Force async** at API boundaries — services, controllers, message handlers. No sync entry points (other than `Main`).
3. **Right-size Min threads** in container deployments. A web app with steady-state 50 concurrent requests should not start at `Min = 2`.

## A diagnostic shortcut

If you suspect starvation in production but can't take a trace, raise `ThreadPool.SetMinThreads` to a generous value (e.g., 200). If latency improves immediately, you have starvation; the underlying bug is sync-over-async or similar. The setting is a workaround; fix the bug.

## Why "more threads" isn't the answer

Raising max threads:

- Each thread costs ~1 MB of stack, so memory grows.
- Context switch cost grows.
- The bug is still there; you've masked the symptom.

Raising min threads at startup *is* OK as a tuning lever for known steady-state concurrency, but it's not a fix for sync-over-async.
