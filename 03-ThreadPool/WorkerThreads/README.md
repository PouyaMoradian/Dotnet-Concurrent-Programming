# Worker threads

Pool workers are general-purpose threads that run user delegates and async continuations. They use the work-stealing deque architecture covered in [01/06-Work-Stealing](../../01-Fundamentals/06-Work-Stealing/), but there are details worth knowing.

## Lifecycle

| State | Trigger |
|---|---|
| Created | Hill-climbing decided to add one, or `SetMinThreads` raised the floor |
| Idle (parking) | Local deque empty, no global work, no steal target |
| Active | Has popped work; runs to completion of that work item |
| Retired | Idle for ~20s; controller decided to shrink |

## How a work item is dispatched

Internally, every "thing the pool runs" implements `IThreadPoolWorkItem`:

```csharp
public interface IThreadPoolWorkItem
{
    void Execute();
}
```

`Task` implements this. So does the boxed `Action` for `ThreadPool.QueueUserWorkItem`. So does the state machine for an async continuation that resumes after IO completes. The pool just calls `Execute()`.

## `UnsafeQueueUserWorkItem` — the allocation-free path

```csharp
ThreadPool.UnsafeQueueUserWorkItem(static (state) =>
{
    // do work; capture nothing
}, state);
```

vs.

```csharp
ThreadPool.QueueUserWorkItem(state => { /* … */ }, state);
```

The "Unsafe" version skips capturing `ExecutionContext`. If your work doesn't need the calling context (rare in a server), this saves an allocation. The other knob: `preferLocal: true` puts work on the *current* worker's local queue — useful inside a worker that wants to chain follow-up work on its hot caches.

## Async continuations

When an `await` on an incomplete `Task` resumes, the continuation runs as a work item on the pool (assuming no captured `SynchronizationContext`). The state machine itself is stored in a heap-allocated object (or pooled, with `[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]` — see [08/AllocationFreeAsync](../../08-Async-Await-Deep-Dive/AllocationFreeAsync)).

## What happens during work execution

A worker thread:

1. Pops a work item.
2. Restores `ExecutionContext` (captured at queue time, unless "Unsafe").
3. Executes — exceptions bubble out into the unobserved-exception path of `TaskScheduler` (there's a process-shutdown safety net via `TaskScheduler.UnobservedTaskException`).
4. Loops back to step 1, or parks if no work.

## Implications

- **Workers are interchangeable.** Don't store thread-local state on the assumption that "the next iteration runs on the same worker" unless you've controlled that with affinity or a custom scheduler.
- **`ThreadStatic` on a worker is a leak risk.** If you store data per-worker, keep it bounded — long-lived workers accumulate it.
- **`AsyncLocal<T>` is per-async-flow, not per-thread**. It rides on `ExecutionContext`. See [08/ExecutionContext](../../08-Async-Await-Deep-Dive/ExecutionContext).
