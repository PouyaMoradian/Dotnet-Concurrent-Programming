# .NET implications of context switching

This file translates the previous three into the daily choices .NET developers make. You'll recognise some of these as folk wisdom ("don't `Thread.Sleep`", "prefer async") — here we make the *why* mechanical.

## `Thread.Sleep` vs `Task.Delay`

```csharp
Thread.Sleep(100);    // blocks this thread for ≥100 ms
await Task.Delay(100); // returns this thread to the pool; resumes after ≥100 ms
```

`Thread.Sleep`:
- Parks the *calling thread*. If you're on a pool worker, you've reduced the pool's capacity by one for the duration.
- The OS schedules the wakeup on a timer at the next tick of the system's timer resolution. On Windows desktop that's typically 15.625 ms; you may sleep ~115 ms when you asked for 100.
- Cannot be cancelled cooperatively.

`Task.Delay`:
- The timer is system-managed (`TimerQueueTimer`); no thread parks.
- The continuation fires on a pool thread. Cancellation via `CancellationToken` works as expected.
- Resolution is similar (subject to the same system tick), but you didn't waste a thread waiting.

For *anything* longer than ~1 ms in async code, `Task.Delay` is the right call.

## `Thread.Yield`, `SpinWait`, `Thread.SpinWait`

Three subtly different operations:

- **`Thread.Yield()`** — give up the rest of the quantum to *any other runnable thread*. Returns true if it actually yielded. Cheap-ish; one syscall.
- **`SpinWait`** — a struct that does an adaptive backoff: spin first, then briefly yield, then sleep. Use this in **CAS** (Compare-And-Swap) retry loops to avoid both pure-spin CPU waste and full kernel parking. Example:

  ```csharp
  var spinner = new SpinWait();
  while (Interlocked.CompareExchange(ref _state, Working, Idle) != Idle)
      spinner.SpinOnce();
  ```

- **`Thread.SpinWait(iterations)`** — pure CPU spin for the given iterations. No yield, no sleep. Used inside `SpinWait` and `lock`'s internal fast path. Don't call directly in app code unless you have a precise need.

## When `lock` is cheap, when it isn't

The fast path of `Monitor.Enter` (which `lock` lowers to) is:

1. Try a `CompareExchange` to put your thread's ID into the object header.
2. On success, return.

That's ~5–10 ns uncontended. No context switch.

The slow path:

1. Spin briefly hoping the lock releases.
2. If it doesn't, allocate a sync-block (if needed), park on the kernel event in it.
3. The releasing thread signals; OS wakes a waiter; resume.

The slow path is a full context switch — typically µs of wall time plus cache disturbance. Two consequences:

- **Don't hold a lock across IO or any long operation.** Anyone waiting parks; the wakeup cost dominates throughput.
- **Don't lock at fine granularity in a hot loop.** Even uncontended, the locked CAS is ~10× a normal store. Aggregate, then lock once at the boundary.

## Timer resolution traps

`System.Threading.Timer`, `PeriodicTimer`, and `Task.Delay` all sit on the OS's timer queue. On Windows the queue resolution depends on the current global timer resolution. Several scenarios:

- **You set up many short-period timers (5 ms).** Some library or browser elsewhere on the system has raised resolution. They fire. Move to another machine where nothing's raised resolution, and now they fire every ~16 ms instead.
- **You're running .NET 8 and noticed timers got more accurate.** .NET 7 raised global timer resolution in some cases; .NET 8 fixed this to be more targeted.
- **You need sub-ms periodicity.** Don't use `Task.Delay`. Use a dedicated thread with `Thread.Sleep(0)` / `SpinWait` (CPU-bound) or, on Linux, a real-time clock via `clock_nanosleep`.

The general principle: **the OS gives you ms precision by default**. If you need µs, you're outside the comfort zone of any garbage-collected, JIT-compiled, scheduler-preempted runtime, and you need to design around the OS.

## Async is not "more threads"

The most persistent misconception about `async`/`await` is that it adds parallelism. It doesn't — it reduces *threads needed per concurrent operation*. Two corollaries:

1. **Async doesn't speed up a CPU-bound workload.** A `for` loop wrapped in `async` runs at the same speed. (You may need `Task.Run` to push CPU work off a request thread, but that adds threads, not async.)
2. **Async dramatically reduces context-switch pressure for IO-bound workloads.** A web server with 10k in-flight requests via async needs ~ProcessorCount threads. Sync would need 10k.

## ThreadPool sizing

Defaults:

- **Min threads** = `Environment.ProcessorCount`. Below this, the pool spawns immediately when work arrives.
- **Max threads** = high (32767 typical). Above this, work queues.
- **Adaptive growth**: between min and max, the pool adds threads slowly (one every ~500 ms) if it observes throughput improving; removes them gradually if idle.

When to override (`ThreadPool.SetMinThreads`):

- **Cold-start storms.** A server that gets hit with a burst of requests at startup might queue work waiting for the pool to grow. Pre-warming with `SetMinThreads(N, N)` makes the first burst fast.
- **Heavy sync-over-async legacy code.** Sometimes the only short-term fix for a thread-starved code path is to set a higher min count.

When *not* to override:

- "Just in case" — overprovisioning costs context switches and memory (1 MB stack per thread).
- To "speed up async" — async doesn't need more threads.

## `ConfigureAwait(false)` and synchronisation contexts

`SynchronizationContext` is "where to resume after `await`". UI frameworks set one (the UI thread); ASP.NET Core does not. When a context is captured:

- The continuation must run on the captured context. If the context is single-threaded (the UI thread), the continuation queues there.
- If the continuation is queued behind blocked work on the captured context, **deadlock**. The classic: `someTask.Result` on the UI thread blocking the same UI thread the continuation is queued to.

`ConfigureAwait(false)` says "I don't care which thread resumes". Use it in library code unconditionally. App code that doesn't touch UI doesn't need it (ASP.NET Core has no context to capture).

The context-switch implication: capturing a context means the continuation likely runs on a *specific* thread, possibly forcing a scheduler dispatch to that thread. Not capturing one means "any pool thread", which is cheaper.

## Practical takeaways

- `await Task.Delay(x)` instead of `Thread.Sleep(x)` in async paths — always.
- Don't take a `lock` across `await` (the C# compiler forbids it — error CS1996; some primitives like `SemaphoreSlim` allow async-friendly waiting instead).
- Keep critical sections short. Aggregate first, lock once.
- For sub-millisecond timing, you're past the OS's design point. Restructure.
- The thread pool is your friend. Don't fight it with raw threads for short-lived work.

## Lab

There's no .NET demo specifically for these patterns in chapter 0; chapter 1's `SyncVsAsyncDemo` is the directly relevant one — it shows 200 fake "requests" served by async with a fraction of the threads.

## Further reading

- **Stephen Toub — *ConfigureAwait FAQ*** — the definitive answer to every question about the keyword.
- **Stephen Toub — *Concurrent threadpool work in .NET*** — design of the hill-climbing controller.
- **Joe Duffy — *Concurrent Programming on Windows*** — chapter 5 on sync primitives.
