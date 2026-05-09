# Custom TaskSchedulers

`TaskScheduler` is the abstraction `Task` uses to decide *where* a task runs. The default scheduler queues to the ThreadPool. You can replace it for specific tasks — typically to enforce ordering, concurrency limits, affinity, or fairness.

## The minimal interface

```csharp
public abstract class TaskScheduler
{
    protected abstract void QueueTask(Task task);
    protected abstract bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued);
    protected abstract IEnumerable<Task>? GetScheduledTasks();
    public virtual int MaximumConcurrencyLevel => int.MaxValue;
    // … helpers: TryExecuteTask, FromCurrentSynchronizationContext, etc.
}
```

You implement `QueueTask` (how/where to run the task) and `TryExecuteTaskInline` (can the current thread run the task right now? — usually return `false` unless you have a really good reason).

## Patterns to know

### Sequential (one-at-a-time)

The chapter's `SequentialScheduler` (see `Demos/CustomSchedulerDemo.cs`) processes a FIFO of tasks, one at a time, on whatever pool thread is free. Useful for "exclusive access to a non-thread-safe component" without locks.

### LimitedConcurrencyLevelTaskScheduler

A canonical sample (in BCL samples for years; an explicit reference impl is in [Stephen Toub's gist](https://devblogs.microsoft.com/pfxteam/)). Caps `MaximumConcurrencyLevel` to N, queueing the rest. Useful for IO-fan-out with a strict cap. **In modern code, prefer `Parallel.ForEachAsync` or a `SemaphoreSlim`** — they're easier to read.

### Synchronisation-context wrapper

`TaskScheduler.FromCurrentSynchronizationContext()` returns a scheduler that posts to the captured `SynchronizationContext`. Used to marshal continuations to a UI thread. In modern desktop apps, `await` does this automatically — explicit use is rare.

## Implementation pitfalls

1. **Don't capture `ExecutionContext` twice.** `Task` already does. Your scheduler should not wrap the delegate further.
2. **Use `TryExecuteTask` from a controlled thread.** Don't call it from a re-entrant context.
3. **Avoid recursion in `TryExecuteTaskInline`.** Many "inline if same scheduler" implementations stack-overflow under contention. Default `false` unless you've designed for it.
4. **Don't lose tasks on cancellation.** The framework catches via `Task.IsCanceled`, but your queue must drain.

## When to skip a custom scheduler

You almost never need one in 2026 .NET. The available alternatives — `Parallel.ForEachAsync`, `Channel<T>`, `RateLimiter`, `TPL Dataflow` — solve the cases that prompted hand-rolled schedulers a decade ago. Reach for a custom scheduler only when you need:

- **Apartment-state affinity** (single-threaded with strict thread identity).
- **A thread pool with hard external constraints** (e.g., a vendor SDK requires all calls on a specific thread).
- **A scheduler that bridges an external event loop** into TPL.
