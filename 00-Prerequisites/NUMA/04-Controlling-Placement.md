# Controlling placement

When defaults aren't enough, NUMA placement can be controlled at three layers: from outside the process (`numactl`, `taskset`, cgroups), from inside the process (process and thread affinity APIs), and from inside .NET (a few environment variables for the GC). This file is the cheat-sheet for each, with the caveats.

## Outside the process — `numactl` (Linux)

The simplest, most-blunt instrument.

```bash
# Run entirely on node 0 (CPUs and memory).
numactl --cpunodebind=0 --membind=0 dotnet run --project 00-Prerequisites

# Interleave allocations across all nodes (good for shared-array workloads).
numactl --interleave=all dotnet run

# Prefer node 0 but fall back if exhausted.
numactl --preferred=0 dotnet run

# Show topology and per-node free memory.
numactl --hardware
```

When to reach for it:
- **Benchmarks**: pin everything to one node so results aren't noise-dominated by where threads happened to run.
- **Production isolation**: dedicate a NUMA node to a high-priority service so it doesn't share cache with neighbours.
- **Investigating asymmetry**: same workload bound vs unbound — the difference tells you the NUMA cost.

When *not* to use it:
- General app deployments where the workload doesn't need it. You'll surprise the next engineer.

## Outside the process — `taskset` (Linux)

`taskset` is CPU-only — it doesn't constrain memory:

```bash
taskset -c 0-15 dotnet run     # restrict to CPUs 0–15
```

Useful when you want CPU pinning without memory binding (memory will still first-touch on whichever node the thread happened to run on).

## Outside the process — Windows equivalents

Windows uses **processor affinity masks** plus **Job objects** for grouping.

```powershell
$proc = Start-Process dotnet "run --project 00-Prerequisites" -PassThru
$proc.ProcessorAffinity = 0x000000FF   # CPUs 0–7
```

For NUMA-specific binding, use `SetThreadGroupAffinity` (P/Invoke) — Windows groups CPUs into 64-CPU "processor groups" for large systems, and NUMA-aware tools sit on top.

## Outside the process — cgroups / Kubernetes

In containerised production you control NUMA via the orchestrator. Kubernetes:

```yaml
# A pod that asks for exclusive CPUs from one NUMA node.
spec:
  containers:
  - name: app
    resources:
      requests:
        cpu: "16"
        memory: "32Gi"
      limits:
        cpu: "16"
        memory: "32Gi"
  topologySpreadConstraints: [...]   # opt into NUMA-aware scheduling
```

Kubernetes 1.18+ supports a `topologyManager` with policies (`single-numa-node`, `restricted`). Enable it on the kubelet; ask for guaranteed **QoS** (Quality of Service — Kubernetes promotes pods with `requests == limits` to the "Guaranteed" class and binds them to specific CPUs). Without these, you may get a pod whose CPUs are split across nodes — death for cache locality.

## Inside the process — .NET APIs

### Process-level

```csharp
using System.Diagnostics;

Process.GetCurrentProcess().ProcessorAffinity = (IntPtr)0b1111_1111;   // CPUs 0–7
```

This mask is system-relative. On a system with CPU groups (Windows, >64 CPUs), this only works in group 0 by default; for the broader case use `SetProcessGroupAffinity` via P/Invoke.

### Thread-level

```csharp
using System.Runtime.InteropServices;

[LibraryImport("kernel32")]
[return: MarshalAs(UnmanagedType.Bool)]
private static partial bool SetThreadAffinityMask(IntPtr hThread, UIntPtr dwThreadAffinityMask);

[LibraryImport("kernel32")]
private static partial IntPtr GetCurrentThread();

// Pin the current thread to CPU 3.
SetThreadAffinityMask(GetCurrentThread(), (UIntPtr)(1u << 3));
```

On Linux, use `pthread_setaffinity_np` via P/Invoke into `libc`. `Thread.BeginThreadAffinity()` exists in .NET but its semantics are *opposite* — it tells the runtime not to migrate the *managed* thread between *OS threads*, which is rarely what you want.

### "Ideal processor" hints

Hints, not mandates:

- Windows: `SetThreadIdealProcessor` — "prefer this CPU but you can run elsewhere".
- Linux: `sched_setaffinity` is hard; for soft hints use `pthread_setaffinity_np` with a mask of one CPU plus neighbours.

Hints are usually the right tool: pinning is brittle (an over-pinned thread starves when its CPU is busy with something higher priority).

## Inside the process — .NET environment variables for the GC

| Variable | Effect |
|---|---|
| `DOTNET_GCNoAffinitize=1` | Disable GC's automatic CPU pinning of GC threads. |
| `DOTNET_GCHeapAffinitizeMask=0xFFFF` | Restrict GC heaps to the given CPU mask. |
| `DOTNET_GCHeapCount=N` | Force N GC heaps. |
| `DOTNET_GCCpuGroup=1` | (Windows) Use CPU groups for very large systems. |

Set these *before* the process starts (e.g., in a wrapper script). Changing them at runtime has no effect.

## Putting it together — a recipe

### "I have a latency-critical .NET service on a NUMA box"

1. Use `numactl --cpunodebind=N --membind=N` to bind to one node.
2. Size the service to the node's CPU count (`Environment.ProcessorCount` will reflect the binding on .NET 8+).
3. Use Server GC (default).
4. Don't override GC env vars unless measurement says to.
5. Run a second instance on node 1 for the *other* customer / shard. Load balance externally.

### "I have a CPU-heavy batch job that should use the whole machine"

1. Don't bind.
2. Run with Server GC; let .NET 8+'s NUMA-awareness do its thing.
3. If you observe high inter-node bandwidth dominating cost, `numactl --interleave=all` is often a single-line ~10–20% improvement.

### "I'm benchmarking; I want zero NUMA noise"

1. `numactl --cpunodebind=0 --membind=0 taskset -c 0-7 dotnet run -c Release`
2. Use BenchmarkDotNet's `[InvocationCount]` and warmup options to wash out first-touch effects.

## What you can't reliably do

- **Migrate a hot object's pages to another node from .NET.** No managed API exists. Linux's `move_pages` syscall can do it, but the .NET GC doesn't expose hooks; you'd be moving pages out from under the GC's allocator.
- **Know "which heap" your object is on.** The runtime decides; the only signal you get is via the diagnostics API.

## Practical takeaways

- Most apps don't need any of this. Default Server GC on default thread pool defaults wins on >95% of workloads.
- When you do need it, `numactl` is the simplest and most powerful tool.
- Inside the process, prefer *hints* (ideal processor) to *mandates* (affinity mask) unless the workload demands.
- Always have a before/after measurement. NUMA tuning that doesn't move a number isn't tuning, it's superstition.

## Lab

Re-run `LocalityDemo` with three configurations:

```bash
dotnet run --project 00-Prerequisites -- 3                                          # default
numactl --cpunodebind=0 --membind=0 dotnet run --project 00-Prerequisites -- 3       # bound
numactl --interleave=all          dotnet run --project 00-Prerequisites -- 3        # interleaved
```

On a multi-node host you should see the bound run with the lowest variance; the interleaved run with the most uniform per-iteration timing; the default run somewhere in between depending on what else was happening on the system.

## Further reading

- **`numactl(8)`** — the man page is short and good.
- **`man 7 numa`** — kernel-level overview.
- **Microsoft Docs — *NUMA Support*** — Windows side.
- **Kubernetes docs — *Control Topology Management Policies on a node***.
