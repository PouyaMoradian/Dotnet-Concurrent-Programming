# The .NET ThreadPool — internals worth knowing

The ThreadPool is one of those pieces of infrastructure most .NET developers use thousands of times a day without ever looking inside. Five minutes of looking pays off: most "why is my app slow / unresponsive / leaking threads" questions trace back to one of the mechanisms below.

## Three pieces working in concert

A modern .NET ThreadPool has three layers:

1. **The worker queues** — work-stealing deques (Chapter 1 piece, see [01-Deques-And-Stealing.md](01-Deques-And-Stealing.md)).
2. **The IO completion mechanism** — IOCP on Windows, epoll/io_uring on Linux. Different threads handle network/file completions vs CPU work, at least conceptually.
3. **The hill-climbing controller** — a feedback loop that decides whether to inject or retire worker threads based on throughput.

## Hill-climbing — injecting threads under pressure

The pool starts with a small number of workers (~`Environment.ProcessorCount`). When work piles up, it considers injecting more — but threads are expensive to create and context-switch, so the pool tries to inject only when it'll help.

The algorithm, simplified:

- Periodically (every ~500 ms or after a "wave" of completed work), measure throughput: tasks-per-second.
- Try adding one worker and observe what happens to throughput in the next sampling window.
- If throughput went up, add another next time.
- If throughput went down or stayed flat, retire the new worker.

This is "hill-climbing" because it walks up the gradient of (worker count → throughput). The pool's view of the world is one-dimensional: it doesn't know that 200 of the queued tasks are blocked on `Thread.Sleep` because the *workers* are sleeping; it only sees that throughput is low. So it injects more workers, which also sleep, and so on. This is exactly why sync-over-async / `.Result` is destructive: the pool can be fooled into growing far beyond useful, and the extra workers ramp up slowly enough (one every ~500 ms) that latency for new requests stays bad for many seconds.

Run `SyncVsAsyncDemo` and watch `Process.GetCurrentProcess().Threads.Count` climb. That's hill-climbing reacting to artificial starvation.

## IO completion — what isn't a worker

On Windows, network IO uses **IOCP (IO Completion Ports)**. The kernel notifies completion events to user mode by waking a thread that's blocked in `GetQueuedCompletionStatus`. .NET runs a small pool of IO-completion threads for this purpose, separate from the worker pool. When `socket.ReadAsync` completes, the IO completion thread picks up the event, finds the matching `Task`, and schedules its continuation onto a worker thread (or runs it inline on the IO thread if it's short).

On Linux, the mechanism is epoll / io_uring on supported kernels. The CLR's runtime maintains a background thread that runs the event loop and dispatches completions analogously.

The practical point: **IO completion threads are not workers**. You can't starve your worker pool by issuing thousands of async network operations — the IO completion side handles them. You can only starve workers with CPU work or with sync-blocked workers.

`ThreadPool.GetAvailableThreads(out int workers, out int io)` reports both numbers. In production, watching `workers` is far more useful than `io` because async IO virtually never saturates the IO completion side.

## The global queue vs the local deques

Tasks created from a non-pool thread go into the global queue. Tasks created from inside a pool worker go into that worker's local deque. The difference matters:

- A task on the global queue is picked up by the *next available* worker. It's FIFO across all workers.
- A task on a local deque is run by *that worker* unless someone steals it. It's LIFO for the owner; FIFO if stolen.

This means a `Task.Run` from `Main` (before the pool has been used) behaves slightly differently from a `Task.Run` inside another task: the former is "global-queued and FIFO", the latter is "local-queued and LIFO". In practice you almost never have to think about it, but it explains why nested fork-join workloads run in *post-order* — children of a parent task run before the parent's continuation.

## Long-running tasks

`Task.Factory.StartNew(work, TaskCreationOptions.LongRunning)` requests a *dedicated thread* for the task — bypassing the pool entirely. The runtime creates a fresh `Thread` for the work and returns a `Task` that completes when it finishes.

Use cases:

- A background loop that runs for the lifetime of the app (e.g., a log flusher).
- A blocking IO loop (e.g., a `socket.AcceptTcpClient` listener that you can't make async).
- Any work item longer than ~1 second that you don't want hill-climbing to react to.

Cost: an OS thread per long-running task. Don't use this for short tasks or you've just reinvented `new Thread`.

## Configuring the pool

`ThreadPool.SetMinThreads(workers, io)` — sets the minimum count before hill-climbing kicks in. Useful if you know you'll have a burst of work at startup and don't want to wait for the pool to inject threads slowly.

`ThreadPool.SetMaxThreads(...)` — sets an upper bound. Rarely the right thing to touch; the default is ~32,767 in modern .NET.

`DOTNET_ThreadPool_MinIOCompletionThreads`, `DOTNET_ThreadPool_MinThreads`, etc. — environment variables for setting minimums without code changes.

A widely-applicable production tip: set `MinThreads` to your worst-case startup burst (e.g., 100 if you fire 100 sub-queries on the first request after warmup). It costs almost nothing — those threads are idle until needed — and removes a class of cold-start latency spikes.

## When to ditch the pool

Two scenarios:

1. **Real-time work.** Pool threads run at normal priority. For an audio loop, you want a dedicated high-priority thread. Use `new Thread` with `Priority = ThreadPriority.Highest`.
2. **CPU-pinned numeric kernels.** A scientific computation that doesn't want hill-climbing perturbing it can use a fixed number of dedicated threads with `Thread.ProcessorAffinity` pinning. Rare; most code doesn't need this.

For everything else, the ThreadPool is the right answer.
