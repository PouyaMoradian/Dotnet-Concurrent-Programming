# The Linux scheduler

For everyday workloads Linux uses **CFS** (Completely Fair Scheduler) on kernels < 6.6, and **EEVDF** (Earliest Eligible Virtual Deadline First) from 6.6+. Both give *proportional fairness*: each task receives CPU time proportional to its weight.

## CFS in 60 seconds

- Every task has a **virtual runtime** (`vruntime`) that increases as it runs, scaled by its weight (so high-priority tasks accumulate `vruntime` more slowly).
- A red-black tree of runnable tasks is kept ordered by `vruntime`.
- The scheduler always runs the task with the **smallest `vruntime`**.

Knobs (per-cgroup, settable via `sysctl`):

| sysctl | Default | Meaning |
|---|---|---|
| `kernel.sched_min_granularity_ns` | 750 µs – 4 ms | Minimum slice a task receives before preemption |
| `kernel.sched_latency_ns` | 6–24 ms | Target time to give *every* runnable task one slice |
| `kernel.sched_wakeup_granularity_ns` | 1–4 ms | Hysteresis on wakeup-preempt-currently-running |

Niceness (`nice -n N`) maps to weight: `n=0` → weight 1024, each step is ~25% bigger. So `n=-5` ≈ ~3× the CPU; `n=5` ≈ 1/3.

## EEVDF (kernel 6.6+)

EEVDF replaces CFS's "smallest vruntime" with **earliest virtual deadline among eligible tasks**, where eligibility is based on having received less than its share so far. The result is the same proportional-fair semantics with better latency for short-running tasks.

You don't need to do anything in .NET to benefit from EEVDF — it's transparent.

## Real-time classes (`SCHED_FIFO`, `SCHED_RR`, `SCHED_DEADLINE`)

For genuine real-time threads (audio, robotics, HFT). Bypass CFS/EEVDF entirely. Require `CAP_SYS_NICE`. **Almost no application code should set these.**

## cgroups (containers)

If your service runs in Docker / Kubernetes, cgroup v2 enforces:

- **CPU quota** (`cpu.max`) — number of µs your cgroup can run per period (default 100 ms).
- **CPU shares** (`cpu.weight`) — relative when the host is over-committed.

This matters for .NET because **`Environment.ProcessorCount` is now cgroup-aware** (since .NET Core 3.0): if the container is limited to 0.5 CPU, you'll see 1 (rounded up) — not the host's 64. The TPL's defaults follow.

You can override with `DOTNET_PROCESSOR_COUNT` if you need to override the heuristic (rare; usually the heuristic is right).

## Useful Linux commands for .NET concurrency debugging

```bash
# Per-thread CPU view
top -H -p $(pgrep -f MyApp)
htop      # then press 'H'

# Scheduler trace for one process
sudo perf sched record -p $(pgrep -f MyApp) -- sleep 5
sudo perf sched latency

# Context switches
pidstat -t -w -p $(pgrep -f MyApp) 1

# Affinity
taskset -cp $(pgrep -f MyApp)
```
