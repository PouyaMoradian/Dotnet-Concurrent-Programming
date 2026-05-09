# ThreadPool Starvation

The single most common production .NET concurrency failure. The pool is full of workers that *can't make progress* — and the work that *would* free them is queued behind them. Throughput collapses; latency exists only as a 99.9th percentile.

## How to recognise it

Symptoms in production:

- Latency P99 climbs while CPU usage is *low* (not pinned).
- `dotnet-counters` shows `threadpool-thread-count` *increasing* steadily, hitting the cap.
- `threadpool-queue-length` grows without bound.
- Memory grows because pending tasks hold references.
- Eventually requests time out.

## The textbook causes

### 1. Sync-over-async

```csharp
public IActionResult Get()
{
    var result = SomeAsyncCall().Result;  // BLOCKS the worker
    return Ok(result);
}
```

The handler ran on a worker. `.Result` blocks that worker. The async continuation must run on *some* worker — and there are now N-1 instead of N. Repeat across requests; the pool fills with blocked workers waiting for continuations that need workers to run. *Deadlock or pool exhaustion follows.*

**Fix:** make the action `async Task<IActionResult>` and `await`.

### 2. Blocking IO disguised as async

```csharp
public async Task<string> GetAsync()
{
    using var fs = new FileStream(path, FileMode.Open);
    using var sr = new StreamReader(fs);
    return await sr.ReadToEndAsync();
}
```

If `fs` was opened *synchronously*, `ReadToEndAsync` may still block under the hood. Open with `useAsync: true` (`new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true)`) or use `File.ReadAllTextAsync`.

### 3. CPU-bound work on the pool with synchronous waits

A `Parallel.For` invoked inside an async handler is CPU-bound and pins workers for the duration. If many requests arrive, every request hijacks all workers. Use a **separate scheduler** for CPU work or queue it elsewhere.

### 4. Long-running tasks without `LongRunning`

A worker that runs for minutes is a worker that's not available for short tasks. Use `Task.Factory.StartNew(... TaskCreationOptions.LongRunning)` (allocates a dedicated thread) or `new Thread(...)` for indefinitely-running work.

## How to repro

The `StarvationDemo` in this chapter reproduces the sync-over-async case. Run with `dotnet-counters` watching pool counters in another terminal to see the staircase of thread injection.

## How to fix

1. **Find every `.Result` / `.Wait()` / `.GetAwaiter().GetResult()`** that's not a `Main` entry-point. Most are bugs.
2. **Set Min = your expected concurrency**. `ThreadPool.SetMinThreads(workers, io)` at process start in containers — particularly important on small cgroups where the default is `ProcessorCount = 2`.
3. **Use `Parallel.ForEachAsync` for IO-fan-out**, not `Parallel.ForEach` of `.Result`.
4. **Use `Channel<T>` + dedicated workers** for sustained background processing instead of an unbounded queue of `Task.Run`s.
5. **Profile.** PerfView → "Wall Time Stacks" gives you the *blocked* threads and what they're waiting on.
