# Context switching — overview

A **context switch** is what the OS scheduler does when it takes one thread off a CPU and puts another on. It costs more than developers usually think, the cost is wildly bimodal, and it's the single biggest reason `async`/`await` beats blocking for IO-bound workloads.

This section walks through the anatomy of a switch, the difference between voluntary and involuntary switches, what Windows and Linux's schedulers actually do, the implications for .NET code, and the tooling to observe it.

## What's in this section

| File | What it covers |
|---|---|
| [01-Anatomy-of-a-Switch.md](01-Anatomy-of-a-Switch.md) | What state actually gets saved and restored; the **TLB** (Translation Lookaside Buffer — the per-core cache of virtual→physical address translations); the **FPU** (Floating-Point Unit, which holds wide vector state too); kernel-entry overhead; **KPTI** (Kernel Page Table Isolation — the Meltdown mitigation); the resulting microsecond budget |
| [02-Voluntary-vs-Involuntary.md](02-Voluntary-vs-Involuntary.md) | The two flavours and why their costs differ in practice |
| [03-OS-Schedulers.md](03-OS-Schedulers.md) | Windows quanta and priority; Linux **CFS** (Completely Fair Scheduler) and its replacement **EEVDF** (Earliest Eligible Virtual Deadline First); how scheduling decisions are made |
| [04-DotNet-Implications.md](04-DotNet-Implications.md) | `Thread.Sleep` vs `Task.Delay`; timer resolution; `SpinWait`; why pool threads stay hot |
| [05-Observation-Tools.md](05-Observation-Tools.md) | `dotnet-counters`, `dotnet-trace`, `perf sched`, **ETW** (Event Tracing for Windows), PerfView |

## The 60-second summary

The work per switch, roughly:

| Cost component | Cycles | Wall time |
|---|---|---|
| Kernel entry (syscall / interrupt) | 100–500 | 30–150 ns |
| Save current thread's registers (GPRs + flags) | tens | ~10 ns |
| Lazy or eager FPU/SIMD save (floating-point + vector registers) | tens to hundreds | 10–100 ns |
| If cross-process: TLB flush (`CR3` is the x86 page-table base register; reloading it switches address spaces) | hundreds | 100–300 ns |
| Pick next thread from runqueue | tens to hundreds | 30–100 ns |
| Restore next thread's registers | tens | ~10 ns |
| **Same-process, hot caches: total** | ~1000–4000 | **~1 µs** |
| **Realistic with cache disturbance** | several thousand | **2–10 µs** |

Two distinct gotchas you'll meet again later:

1. The *direct* cost (kernel + register save/restore) is ~1 µs. The *indirect* cost (the cache the previous thread warmed for itself, now cold for the new one) often dwarfs it. A heavily-multithreaded workload can lose 20–40% of throughput to context-switch-induced cache thrashing alone.
2. The OS's notion of "1 ms" is much coarser than you'd expect. A timer fired with 1 ms requested may not wake up for ~15.6 ms on a default Windows. `Thread.Sleep(1)` is famously misnamed.

## Where this shows up in .NET

- **`async`/`await` avoids switches.** When an `await` parks a state machine, the thread returns to the pool to do other work. No switch happens until the awaited operation completes. Compare to a blocking `WaitOne` — the thread parks, costing a switch when work resumes.
- **`Thread.Sleep(1)` ≠ "sleep 1 ms".** It's "sleep at least the current timer resolution", typically ~15.6 ms.
- **`lock`'s fast path doesn't switch.** It spins briefly, then upgrades to a kernel wait if contended. The upgrade is what costs.
- **`Task.Run` enters a thread pool worker.** Pool workers are kept warm so the next dispatch doesn't pay thread-creation cost (~150 µs).
- **Heavy preemption shows up as `lock-contention-count` and a high voluntary-switch rate.** See [05-Observation-Tools.md](05-Observation-Tools.md).

## The big rule

If your workload is IO-bound, prefer async — the thread doesn't sleep, so no switch.
If your workload is CPU-bound, prefer worker threads sized to physical cores — every extra thread is a forced switch.
If your workload is mixed, do both, and measure.

## Demos in this chapter that exercise this section

- **`ContextSwitchDemo`** (demo 2) — ping-pong two threads through a `ManualResetEventSlim`; measure round-trip cost.
- **`BranchPredictionDemo`** (demo 4) — observes pipeline behaviour *between* switches; an interrupted run shows different numbers, which is itself a context-switch lesson.

## Further reading

- **Brendan Gregg — `brendangregg.com`** — every blog post about scheduler observability is gold.
- **Microsoft Docs — *Scheduling priorities*** (Windows) and **`man 7 sched`** (Linux).
- **Joe Duffy — *Concurrent Programming on Windows*** — chapter 5 is the canonical .NET-flavoured treatment.
