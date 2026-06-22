# Anatomy of a context switch

When the OS scheduler swaps thread A off a CPU and puts thread B on, it's not a single instruction; it's a small program. The exact details vary by OS and architecture, but the steps below happen in some form on Windows, Linux, and macOS, on both x86-64 and ARM64.

## The sequence, step by step

1. **Entry to kernel mode.** Something — a syscall, a hardware interrupt, an exception, a timer — transfers control to the kernel. On x86-64 this is `syscall` (~30 ns of pipeline drain plus mode switch) or an **IDT** (Interrupt Descriptor Table) vector for interrupts. On ARM64 it's `svc` / `eret`.

2. **Save thread A's CPU state to its kernel stack.** General-purpose registers, flags register, instruction pointer, segment selectors. ~16 **GPRs** (general-purpose registers) × 8 bytes = 128 B; tens of cycles.

3. **Save floating-point / SIMD state.** XMM/YMM (and ZMM on AVX-512) registers are ~512 B to 2 KB depending on extension. Modern Linux (kernel ≥ 4.9) and Windows save this **eagerly** on every switch — the older *lazy FPU save* optimisation (FPU = Floating-Point Unit, which on modern x86 also covers the SIMD vector registers; save only when the new thread first touches it) was abandoned after the Lazy FP State Restore side-channel vulnerability (CVE-2018-3665, 2018). Eager save is kept cheap by `XSAVEOPT`/`XSAVES`, which write out only the components actually modified.

4. **Possibly update the page-table base register.** If thread B belongs to a *different* process, the kernel reloads `CR3` on x86 (the page-table base register; reloading it switches address spaces) or `TTBR0` on ARM. Doing this *flushes the TLB* (Translation Lookaside Buffer — the per-core cache of virtual→physical address translations) on the old generation of CPUs; modern CPUs use **PCID** (Process-Context IDentifiers) so that TLB entries are tagged with which CR3 they belong to and survive the swap. Even with PCID, the page walker for the new process is cold.

5. **Decide which thread to run next.** This is the scheduler's job. On Linux's **CFS** (Completely Fair Scheduler), choose the leftmost node in a red-black tree of runnable threads keyed by `vruntime`. On Windows, pull from the highest-priority non-empty ready queue. The work is small but non-zero, especially on systems with many runnable threads.

6. **Restore thread B's CPU state from its kernel stack.** Symmetric to step 2.

7. **Return to user mode.** `sysret` / `eret`. Resume at the instruction B was about to execute when it last got descheduled.

## The Meltdown / KPTI tax

Pre-2018, the kernel and the user-space process shared a page table — the kernel's mappings were just marked "supervisor only". The Meltdown vulnerability showed that a user process could speculatively read kernel memory it shouldn't be able to. Mitigation: **separate page tables for kernel and user space** (on Linux this is called **KPTI** — Kernel Page Table Isolation, originally also known as **KAISER** — Kernel Address Isolation to have Side-channels Efficiently Removed; on Windows it's called *KVA Shadow*). Every syscall and every interrupt now switches CR3 twice (kernel page table on entry, user page table on exit).

The performance impact, as of 2026, depends on hardware: pre-Skylake parts pay 5–15% on syscall-heavy workloads; modern parts with PCID + the Indirect Branch Prediction Barrier mitigations pay 1–5%. .NET 8+ specifically avoids unnecessary syscalls on hot paths to keep this small.

## What "the cache disturbance cost" actually is

When thread B starts running where thread A was running:

- The new thread's instruction stream isn't in L1i — front-end stalls until it warms up.
- The new thread's working set isn't in L1d/L2 — every load misses initially.
- The TLB is probably warm (PCID) or cold (no PCID); either way, the page-walker may run.

If A was on the CPU for a full quantum and warmed 200 KB of cache for itself, that's 200 KB of L2 that's no longer useful and will be evicted as B fills its own working set. When A comes back, A has to re-warm.

This is why **high context-switch rates correlate with high cache-miss rates** and is one of the things you'll see in `perf stat` or BenchmarkDotNet's hardware counters when a benchmark is being interrupted by other system load.

## Same-process vs cross-process switches

| Switch type | Direct cost | TLB impact | Cache impact |
|---|---|---|---|
| Same-process, same-core | ~1 µs | none (same CR3) | minimal (warm) |
| Same-process, cross-core | ~1 µs | none | the new core's L1/L2 are cold for this thread |
| Cross-process, same-core | ~2 µs | TLB tagged or flushed depending on PCID | invalidation through different mappings |
| Cross-process, cross-core | ~2 µs | as above | cold caches on the new core |
| Cross-socket (NUMA-remote) | ~5–10 µs | same | DRAM reads now go cross-socket |

The implication: **thread affinity helps**. A thread that keeps coming back to the same core finds its cache lines waiting. The kernel scheduler tries to maintain affinity, but isn't guaranteed to. For latency-critical paths, set affinity explicitly:

```csharp
// Linux: P/Invoke into sched_setaffinity
// Windows: Process.GetCurrentProcess().ProcessorAffinity = (IntPtr)0x01;   // core 0
```

(Don't do this in normal code — you'll fight the kernel's own balancing. Do it on dedicated threads with a clearly defined CPU budget.)

## What ".NET's thread pool" does about it

The pool keeps worker threads alive between work items. A finished work item *doesn't* terminate the thread; it returns the thread to the pool. The next work item picks up an already-warm thread. The pool gradually retires unused threads on a timer (default 15 s idle).

The pool's *adaptive growth* algorithm (the hill-climbing heuristic) tries to add and remove threads to maximise throughput. On heavily contended workloads it can over-provision; on heavily IO-bound async workloads it stays small because the threads aren't actually doing work. This is a deliberate design — chapter 03 covers it in depth.

## Practical takeaways

- **A switch costs ~1 µs direct + several µs indirect.** That's a budget you can't ignore for sub-millisecond paths.
- **Same-process switches are cheap; cross-process are not.** Multi-process designs pay more.
- **Affinity matters for latency.** Letting the kernel migrate your thread between cores costs cache warmup.
- **The thread pool exists to amortise these costs.** Use it; don't make raw `Thread` objects for short-lived work.

## Lab

```bash
dotnet run --project 00-Prerequisites -- 2
```

`ContextSwitchDemo` measures a round-trip ping-pong. On a quiet desktop expect ~2–6 µs per round-trip via `ManualResetEventSlim`. On Linux, prepend `taskset -c 0 dotnet ...` to pin both threads to one core; the warm-cache version should drop noticeably. Use `taskset -c 0,7` to force cross-core; expect higher numbers.

## Further reading

- **`man 2 sched_yield`** and **`man 7 sched`** — the Linux-side authority.
- **Microsoft Docs — *Context Switches*** under Windows internals.
- **Brendan Gregg — *Linux Performance*** — chapter on schedulers and observability.
