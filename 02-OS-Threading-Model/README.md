# 02 — The OS Threading Model

> **Layer:** Operating system
> **Reading time:** ~25 minutes
> **Prereq:** [00](../00-Prerequisites/), [01](../01-Fundamentals/)

A managed `Thread` is one-to-one with an OS thread. So before the CLR makes any decisions, the kernel has already decided who runs, when they run, and on which core. This chapter is about *that* layer — the rules of the game the CLR plays inside.

## What the OS scheduler actually does

Both Windows and Linux use **preemptive, priority-based, multi-level feedback** schedulers. Boiled down:

1. Maintain a set of **ready** threads. Each has a priority.
2. Pick the highest-priority ready thread for each idle CPU.
3. Let it run for up to a *quantum*; preempt when the quantum expires or when a higher-priority thread becomes ready.
4. Apply anti-starvation: low-priority threads occasionally get a "boost".

There are deep differences in the details — see [Windows-Scheduler](Windows-Scheduler/) and [Linux-Scheduler](Linux-Scheduler/).

## Kernel mode vs user mode

Every system call (file IO, network, locking primitives that escalate to kernel waits, GC suspension on threads in P/Invoke) crosses the kernel boundary. Crossing costs ~100–500 cycles on modern hardware (more after Spectre / Meltdown mitigations like KPTI/KAISER). It's why:

- **Synchronisation primitives have a "fast path" in user mode** (`Monitor`'s spin, `SemaphoreSlim` first, then escalates to a kernel wait if contended).
- **`Thread.Sleep(0)` ≈ "yield to another runnable thread of equal or higher priority"** — entirely a user-mode hint on Windows.
- **`SpinWait` exists** to amortise the cost of going to kernel: spin a bounded number of times before parking.

See [Kernel-vs-User-Mode](Kernel-vs-User-Mode/).

## Hyper-Threading / SMT

Modern CPUs expose 2 logical processors per physical core. They share execution units, L1 caches, and the branch predictor. Two CPU-bound threads on the same core compete; you do not get 2× throughput.

**Implication for benchmarking:** if `Environment.ProcessorCount` is 16 on an 8-core/16-thread CPU, the practical CPU-bound parallelism ceiling is closer to 8–10×, not 16×. Verify with [`HyperThreading`](HyperThreading/).

## CPU affinity

Pinning a thread to a specific core (or set of cores) reduces migration cost and helps NUMA. .NET exposes:

```csharp
using var p = Process.GetCurrentProcess();
p.ProcessorAffinity = (IntPtr)0b0001;       // run on logical CPU 0 only
```

For per-thread affinity you must P/Invoke (`SetThreadAffinityMask` on Windows, `pthread_setaffinity_np` on Linux). See [CPU-Affinity](CPU-Affinity/).

**Use sparingly.** Pinning is right for HFT, real-time-ish audio, or NUMA-strict workers. It's almost always wrong for general server code, where you fight the scheduler instead of cooperating with it.

## In-chapter folders

| Folder | Topic |
|---|---|
| [Windows-Scheduler](Windows-Scheduler/) | Quanta, priority boosts, the Windows 10/11 scheduler |
| [Linux-Scheduler](Linux-Scheduler/) | CFS, EEVDF (kernel 6.6+), cgroups, niceness |
| [Kernel-vs-User-Mode](Kernel-vs-User-Mode/) | Syscall cost, futexes, fast paths in sync primitives |
| [HyperThreading](HyperThreading/) | SMT internals, when it helps, when it hurts |
| [CPU-Affinity](CPU-Affinity/) | Pinning processes/threads, observing migrations |

## Run

```bash
dotnet run --project 02-OS-Threading-Model
```
