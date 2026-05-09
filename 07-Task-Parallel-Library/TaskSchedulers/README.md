# TaskSchedulers

The default `TaskScheduler.Default` queues to the `ThreadPool`. You can supply a custom scheduler when:

- You need **ordered/sequential** execution of tasks that don't share an explicit lock.
- You need a **concurrency cap** stricter than what the pool gives.
- You need **affinity** — all tasks must run on a specific thread.
- You need **bridge** to a foreign event loop (e.g., a vendor SDK that demands all calls on its thread).

For most other "I want to control concurrency" needs, **`Parallel.ForEachAsync`, `SemaphoreSlim`, `Channel<T>`, or `RateLimiter`** are simpler.

## TaskScheduler.Current vs Default

```csharp
var ambient = TaskScheduler.Current;     // whatever the surrounding context picked
var pool   = TaskScheduler.Default;      // always the ThreadPool scheduler
```

`Current` *can* be a UI scheduler if `Task.Factory.StartNew(...)` is called from a UI context. This is a footgun — `Task.Factory.StartNew(action)` will queue to the UI thread, not the pool, in WinForms/WPF code. **Always pass `TaskScheduler.Default` explicitly when you mean the pool.**

## LimitedConcurrencyLevelTaskScheduler

A canonical "max N tasks at once" scheduler:

```csharp
public sealed class LimitedConcurrency : TaskScheduler
{
    private readonly LinkedList<Task> _tasks = new();
    private readonly int _max;
    private int _running;

    public LimitedConcurrency(int max) => _max = max;
    public override int MaximumConcurrencyLevel => _max;

    protected override void QueueTask(Task task)
    {
        lock (_tasks) _tasks.AddLast(task);
        TryRun();
    }

    private void TryRun()
    {
        if (Interlocked.Increment(ref _running) > _max) { Interlocked.Decrement(ref _running); return; }
        ThreadPool.UnsafeQueueUserWorkItem(_ =>
        {
            try
            {
                while (true)
                {
                    Task? t;
                    lock (_tasks)
                    {
                        if (_tasks.First is null) return;
                        t = _tasks.First.Value; _tasks.RemoveFirst();
                    }
                    TryExecuteTask(t);
                }
            }
            finally { Interlocked.Decrement(ref _running); }
        }, null);
    }

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;
    protected override IEnumerable<Task> GetScheduledTasks() { lock (_tasks) return _tasks.ToArray(); }
}
```

Use:

```csharp
var sched = new LimitedConcurrency(4);
var factory = new TaskFactory(sched);
var tasks = items.Select(i => factory.StartNew(() => Process(i))).ToArray();
await Task.WhenAll(tasks);
```

For modern code, prefer `Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (i, ct) => …)`.

## Sequential scheduler

See [03-ThreadPool/CustomSchedulers](../../03-ThreadPool/CustomSchedulers/) for a complete implementation. Useful when you have a non-thread-safe component and want exclusive access without locking.

## Pitfalls

1. **Inlining (`TryExecuteTaskInline`)** is dangerous — if your scheduler runs work inline on the queueing thread, ordering and re-entrance assumptions break. Default to `return false`.
2. **`GetScheduledTasks`** is for the debugger. Make it return a snapshot, never the live collection.
3. **Scheduler ≠ ExecutionContext.** A custom scheduler doesn't change `ExecutionContext` flow; that's still the framework's job.
