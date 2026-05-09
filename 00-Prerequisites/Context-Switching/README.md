# Context switching

A **context switch** is what the OS scheduler does when it takes one thread off a CPU and puts another on. It costs more than developers usually think, and the cost is wildly bimodal.

## What gets saved and restored

| Component | Cost |
|---|---|
| General-purpose registers + flags | A handful of cycles |
| FPU / SIMD state (XMM/YMM/ZMM, ARM SVE) | Tens of cycles, sometimes lazy-saved |
| Page table base register (CR3 on x86) on cross-process | TLB invalidation → many hundreds of cycles |
| Kernel-mode entry/exit | ~100–500 cycles (mitigated post-Spectre by KPTI/KAISER which adds more) |

That gives a **lower bound** for a context switch on the same CPU, with hot caches, of ~1 µs. The realistic average on a busy server is **2–10 µs** when you include cache disturbance — the new thread cold-misses on lines the previous one had warmed.

## Why this matters for .NET

- **`Thread.Sleep(1)` is not "sleep for 1 ms"** — it's "stop running for at least the OS scheduler's tick quantum, which on Windows is usually ~15.6 ms unless something has called `timeBeginPeriod`."
- **Synchronous waiting is expensive** — every `lock` that contends causes a sleep + wake. SpinLock and Monitor's lightweight spinning exist precisely to amortise this for short critical sections.
- **Async beats sync for IO-bound code** — because `await` doesn't park the thread; it returns it to the pool.

## Fairness and quanta

Both Windows and Linux use preemptive, priority-based schedulers with anti-starvation. The default thread *quantum* (the amount of CPU time before forced preemption) is roughly:

| OS | Default quantum |
|---|---|
| Windows desktop | ~20 ms (3 quanta of ~6.6 ms each on a `Thread`) |
| Windows server | ~120 ms (foreground apps) |
| Linux CFS | dynamic; `sched_min_granularity_ns` ≈ 1 ms; `sched_latency_ns` ≈ 6 ms |

Inside a quantum, a thread keeps running unless it blocks (calls a blocking syscall) or yields. After a quantum, the scheduler picks another runnable thread.

## Voluntary vs involuntary switches

- **Voluntary**: `await Task.Delay`, `lock` contention, IO. Cheap-ish — the thread chose to give up the CPU.
- **Involuntary**: scheduler preempts you mid-work. Worst-case for caches because your hot lines are now cold.

You can observe both with `dotnet-counters monitor` watching `System.Runtime / threadpool-thread-count` and your OS's per-thread CPU stats.

## Lab

Run the chapter 00 program and pick the *Context switch ping-pong* demo. On a quiet desktop, expect ~2–6 µs per round trip via `ManualResetEventSlim`. Pin the process to one core (`taskset -c 0`) and you should see it drop — same-core has hot caches.
