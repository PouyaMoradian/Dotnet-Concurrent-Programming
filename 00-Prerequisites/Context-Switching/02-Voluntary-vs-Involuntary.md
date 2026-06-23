# Voluntary vs involuntary context switches

The total context-switch *cost* is roughly the same in both flavours, but the *circumstances* differ in ways that affect the program's overall behaviour, the cache aftermath, and what shows up in your monitoring.

## Voluntary

A thread voluntarily gives up the CPU when it has no more work to do *right now*. Common causes in .NET:

- `await Task.Delay(...)` — schedules a resumption later; the thread returns to the pool.
- `await someIO` — the IO completion is what'll re-queue the continuation.
- `Thread.Sleep(...)` — the thread parks until the OS wakes it.
- `lock` contending on an already-held monitor — after short spin, the thread parks.
- `SemaphoreSlim.WaitAsync()` past the count — async-park or sync-park depending on overload.
- Any blocking `WaitOne` / `Wait` / `Read` / `Write` against a kernel handle.

What happens in the kernel:

1. The thread executes a syscall (futex/`WaitForSingleObject`/select/epoll/IO completion).
2. The kernel marks it not-runnable and schedules someone else.
3. The scheduler doesn't need to *preempt* — the thread already left voluntarily.

The aftermath: the new thread doesn't suffer from the *previous* thread's caches because the previous thread might come back to the same core when it wakes. On Linux's **CFS** (Completely Fair Scheduler), runqueue affinity is preserved by default.

## Involuntary

An involuntary switch is preemption — the scheduler decides "you've had enough CPU" or "someone higher priority showed up". Common causes:

- The thread used its full quantum. On Windows client that's ~30 ms (2 clock intervals), boosted up to ~90 ms for the foreground app; on Linux a CFS time slice is typically a few ms; on Windows server ~180 ms. (See [03-OS-Schedulers.md](03-OS-Schedulers.md).)
- A higher-priority thread became runnable (e.g., an IO interrupt unblocked a service thread).
- Inter-processor interrupts for load balancing (the scheduler moving threads between cores to balance load).

The aftermath is worse than voluntary:

- The interrupted thread had hot caches *for what it was doing*. Those are now wasted for the new thread.
- The interrupted thread might be in the middle of a critical section. Other threads waiting on the lock now have to wait for the preempted thread to be rescheduled.

The second point is the "**lock convoy**" hazard. Imagine 8 threads contending on a lock. One acquires it. The OS preempts that thread. Now 7 threads are queued on a lock held by a *non-running* thread. They wait the full quantum (or until the preempted thread becomes runnable again, whichever comes first). Throughput collapses.

## Why this matters for async

The single most important reason `async`/`await` wins for IO-bound workloads:

- Sync: a blocked thread sits on the runqueue (well, off it, in a wait state). The pool needs another thread to serve more requests. With 1000 concurrent IO operations, you need 1000 threads — each costing 1 MB of stack and ~150 µs to create, and each forcing a context switch when something completes.
- Async: a blocked operation doesn't park a thread. The thread returns to the pool. The pool stays small (~ProcessorCount). When the IO completes, an arbitrary pool thread picks up the continuation.

In a sustained heavy-load test on a 16-thread box, the sync model often peaks at ~1500 **RPS** (requests per second) before context-switch overhead saturates. The async model on the same box happily does 30k RPS. The difference is almost entirely context-switch and stack pressure.

## Observing the two

`dotnet-counters monitor System.Runtime`:

```
threadpool-thread-count                 24
threadpool-completed-items-count        1,294,820 (delta)
threadpool-queue-length                 0
threadpool-thread-count (sustained)     # rising under load
```

On Linux, look at `/proc/<pid>/status`:

```
voluntary_ctxt_switches:       2491200
nonvoluntary_ctxt_switches:    151
```

A healthy async server has voluntary switches by the bucketload (every IO completion is one) and almost no involuntary ones.

A sync server under load typically shows the inverse — voluntary switches per IO + many involuntary ones from quantum expiry under contention.

`perf stat -e context-switches,cpu-migrations ./yourapp` on Linux gives a clear summary.

## Mitigation strategies

If you're seeing a high involuntary rate:

- **Reduce the number of runnable threads.** Don't oversubscribe — `ThreadPool` with `MinThreads = ProcessorCount` is the default for a reason.
- **Use async for IO.** Stop creating threads to wait.
- **Use cooperative cancellation, not `Thread.Abort`.** Abort no longer works in .NET Core / .NET 5+ anyway.
- **Keep critical sections short.** A preempted lock-holder is the lock-convoy seed.
- **For deterministic latency**, pin threads to cores (`ProcessorAffinity`) *and* set a high priority — but only in fully owned environments (HFT — high-frequency trading; audio; simulation). On shared boxes you'll fight other tenants and the OS.

## Practical takeaways

- Voluntary switches are an architectural feature, not a bug. Async code generates a lot of them, and that's fine.
- Involuntary switches are friction. High rates mean you have more runnable threads than cores can serve.
- The pool's adaptive sizing tries to find the sweet spot. Help it by writing async code on IO and pool-sized threads on CPU.

## Lab

The chapter 0 `ContextSwitchDemo` uses voluntary switches (each side `Wait`s on an event, releasing the CPU). To see an involuntary version, write a tight `while (true) ;` on two threads pinned to one core: each will preempt the other at quantum expiry. The `top` / `htop` output will show ~50/50 utilisation; `perf stat` will show high context-switches.

## Further reading

- **`Documentation/scheduler/sched-design-CFS.txt`** in the Linux kernel source.
- **Russinovich et al. — *Windows Internals*** — chapter on the dispatcher.
- **Brendan Gregg — *Linux Performance* — `cputop` and `runqlat` BPF tools** for measuring runqueue latency.
