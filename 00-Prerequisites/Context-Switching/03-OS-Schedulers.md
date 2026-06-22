# OS schedulers — Windows and Linux

You can write perfectly correct .NET code without ever knowing how Windows's dispatcher or Linux's **CFS** (Completely Fair Scheduler) work. But the moment you're trying to explain *why* a benchmark moved 30% when nothing about the code changed, or why `Thread.Sleep(1)` slept for 16 ms, the answer is in the scheduler. This file is the short, useful version.

## Windows: the dispatcher

The Windows scheduler is **preemptive**, **priority-driven**, and **per-CPU runqueue**. Each thread has a *priority* (0–31) and a *base priority* derived from its process priority class and its own setting. The scheduler always runs the highest-priority runnable thread.

| Priority class | Base | Use |
|---|---|---|
| Idle | 4 | Background defrag, indexing |
| Below normal | 6 | Lower-than-default user work |
| Normal | 8 | Default for user apps |
| Above normal | 10 | Foreground UI threads |
| High | 13 | Time-sensitive |
| Realtime | 24+ | Drivers, audio, kernel |

Inside a priority class the scheduler does round-robin with a *quantum*:

| OS | Default quantum |
|---|---|
| Windows client | base quantum = 2 clock intervals (a clock interval is ~15.6 ms, so ~30 ms); the foreground app's threads get up to 3× (~90 ms). UI threads also get a boost on input events |
| Windows server | 12 clock intervals (~180 ms) — long quanta favour throughput over latency |

Quanta can be tuned via the registry (`Win32PrioritySeparation`) but rarely should be in production code.

Windows also has **boosts**: when a thread becomes runnable after waiting on an event, its priority temporarily rises by 1–2. This reduces wakeup latency for IO-completion threads. The boost decays one priority level per quantum it runs.

### Why `Thread.Sleep(1)` is misleading

The default Windows timer resolution is **15.625 ms** (1000/64 Hz). A sleep less than that rounds *up* to the next tick. So `Thread.Sleep(1)` sleeps ~16 ms typically.

You can raise resolution to 1 ms via `timeBeginPeriod(1)` (a C function in `winmm.dll`). The .NET BCL sometimes does this for you (`System.Threading.Timer` historically did; .NET 8 reduced this). But:

- Raising resolution increases interrupt rate system-wide. Battery life suffers on laptops.
- Other processes benefit too (timer resolution is global), which can mask scheduling sensitivity in tests.

The right answer for sub-15 ms waits is **don't** — use a `ManualResetEventSlim`, an `await Task.Delay` with a `TimerQueueTimer`, or restructure to event-driven.

## Linux: CFS (and, on 6.6+, EEVDF)

Linux has used the **Completely Fair Scheduler (CFS)** since 2007 and is migrating to **EEVDF** in 6.6+. Both are *virtual-time* schedulers:

- Each runnable thread has a `vruntime` (virtual runtime).
- Pick the thread with the smallest `vruntime` to run next.
- While running, `vruntime` accumulates at a rate inversely proportional to the thread's *weight* (priority).

This is "fair" in the sense that every thread of equal weight gets the same wall-clock CPU over the long run. Higher-priority threads (negative `nice` values) have larger weights and accumulate `vruntime` slowly, so they're picked more often.

Key tunables:

| Knob | Default | Meaning |
|---|---|---|
| `sched_min_granularity_ns` | ~0.75 ms | Minimum time a thread runs before someone else can preempt it |
| `sched_latency_ns` | ~6 ms | Target latency: time within which every runnable thread should run once (scales up with many CPUs) |
| `sched_wakeup_granularity_ns` | ~1 ms | A waking thread can preempt a current one only if its `vruntime` is at least this much lower |

On a system with many runnable threads, the wakeup granularity prevents constant preemption thrash; on a quiet system it makes wakeups fast.

EEVDF (Earliest Eligible Virtual Deadline First) replaces CFS's red-black tree of `vruntime` with a deadline-based picker; the goal is better latency for interactive threads while keeping CFS's fairness. The user-space experience is broadly the same.

### `taskset`, `cgroups`, and SCHED_FIFO

Linux gives you sharp tools:

- **`taskset -c 0,1 ./yourapp`** — restrict to specific cores.
- **`numactl --cpunodebind=0 --membind=0 ./yourapp`** — restrict to a NUMA node.
- **`chrt -f 50 ./yourapp`** — run under `SCHED_FIFO` (a first-in-first-out realtime scheduling class) at priority 50. The thread runs until it yields, blocks, or a higher-priority **RT** (real-time) thread shows up. Use with extreme care; an RT thread that loops forever locks the system.
- **Container cgroups** — Kubernetes and Docker constrain CPU/memory per container. Your .NET app sees `Environment.ProcessorCount` based on the cgroup quota (.NET 8+ honours `cpu.shares` and `cpu.cfs_quota_us`).

In containerised production, the scheduler is often the source of mystery latency: CPU throttling under cgroup quotas, the OS scheduler choosing your container's worker over another container's based on weights you didn't set, etc. Always check `dotnet-counters` and the cgroup metrics together.

## SMT and the scheduler

Both Windows and Linux know about **SMT** (Simultaneous Multi-Threading — Intel's Hyper-Threading and AMD's SMT). When given a choice, the scheduler prefers to place runnable threads on *different physical cores* before doubling up on a single physical core's two logical threads. This is why a 16-thread CPU running 8 CPU-bound threads usually shows ~50% utilisation across 8 *physical* cores, not "50% on each of 16 logical".

When the scheduler does have to share a physical core, the two threads compete for the ALU, L1, and store buffer. Throughput per thread drops to ~60–80% of what a thread alone on the core would see. This is the difference between "logical processors" and "physical headroom" that we called out in the chapter index.

## Interaction with .NET's thread pool

The .NET ThreadPool sets `MinThreads = Environment.ProcessorCount` by default. It manages its own size via a hill-climbing controller observing throughput. On Linux 6.6+ with EEVDF and on Windows, the scheduler cooperates well: the pool's voluntary parking lets the scheduler maintain locality.

Two pathologies to know:

1. **Thread starvation.** If many pool threads are blocked sync-waiting on each other (deadlock or near-deadlock), the pool grows toward its max. Once at max, no new work runs until something completes. `ThreadPool.GetAvailableThreads(out int worker, out int io)` is the diagnostic.
2. **Spin starvation under high contention.** A `lock` that spends most of its time spinning hogs CPU but accomplishes nothing. The OS won't deschedule because the thread is "running". Use `SemaphoreSlim` or a real wait for long-held locks.

## Practical takeaways

- The scheduler is doing its best with the information you gave it. Most "the scheduler is broken" stories are "we asked for 32 threads on an 8-core box".
- Pin and prioritise only for latency-critical paths. For general server code, defaults win.
- Use the right *unit* of wait: events for sub-µs, async for ms, `Thread.Sleep` for "minutes I really don't care about exact timing".
- On Linux, `runqlat` (BPF tool) measures runqueue latency — how long runnable threads waited to get on CPU. High runqueue latency is the smoking gun for oversubscription.

## Lab

There's no .NET demo specifically for scheduler behaviour — the OS picker is too noisy. The closest experiment: run `ContextSwitchDemo` (demo 2) twice, once with `taskset -c 0` (same core, hot caches) and once with `taskset -c 0,7` (cross-core). The same-core run is faster — the scheduler isn't migrating, the caches stay warm.

## Further reading

- **`Documentation/scheduler/sched-design-CFS.txt`** in the kernel source.
- **`man 7 sched`** and **`man 2 sched_setattr`** — the user-space contract.
- **Mark Russinovich, *Windows Internals*** — chapter on dispatcher and CPU scheduling.
- **Brendan Gregg — *Linux Performance Tools*** — `runqlat`, `cpudist`, `offcputime`.
