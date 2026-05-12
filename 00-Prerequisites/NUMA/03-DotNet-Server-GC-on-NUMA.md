# .NET Server GC on NUMA

The .NET garbage collector is configurable in two main modes: **Workstation** (one GC thread, one heap, one set of segments) and **Server** (one heap per logical CPU, parallel GC threads). For multithreaded server workloads, Server GC is almost always the right answer — and it's the default in this repo's `Directory.Build.props`:

```xml
<ServerGarbageCollection>true</ServerGarbageCollection>
<ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
```

Server GC has implications on NUMA that are worth understanding even if you never tune the runtime.

## What Server GC actually does

- **N heaps**, one per logical CPU (`Environment.ProcessorCount`).
- **N GC threads**, one per heap, all running mark/sweep in parallel during a GC.
- **Per-CPU allocation context.** Each application thread is associated with a "home" heap (often the one matching its current CPU); allocations bypass synchronisation by going to that heap's bump-pointer cursor.

The result: allocation is contention-free in steady state. Two app threads on different cores allocate from different heaps without touching each other's data. GC pauses are *slightly* longer but parallelised.

## The NUMA story

When Server GC was first written, it treated all heaps as equivalent — heap *N* was just "the Nth one". On a NUMA box this meant a heap allocated by thread T on node 0 might be served by GC thread *N* running on node 1 during a GC, with all that node 0 memory now being accessed remotely.

Modern .NET improved this incrementally:

| .NET version | NUMA awareness in Server GC |
|---|---|
| 4.6 (legacy) | Basic awareness; per-CPU heaps allocate from local nodes (with first-touch). |
| Core 3.x | Improvements to heap balancing. |
| 5–7 | Refinements; per-heap allocation segments tend to be local. |
| 8 | Significant improvements: GC threads pinned to nodes, segments allocated node-locally. |
| 9 | DATAS (Dynamically Adapting To Application Sizes) — the heap count and size adjust dynamically; still NUMA-aware. |

The key environment variables (when you need to override):

| Variable | Effect |
|---|---|
| `DOTNET_GCHeapCount=N` | Force a specific number of GC heaps (0 = auto). |
| `DOTNET_GCNoAffinitize=1` | Disable GC's affinity logic (rarely needed). |
| `DOTNET_GCCpuGroup=1` | Use Windows CPU groups for systems with >64 logical processors. |
| `DOTNET_GCHeapAffinitizeMask` | Bitmask of CPUs to spread heaps across. |
| `DOTNET_GCDynamicAdaptationMode=1` | Enable DATAS on .NET 8 (default in .NET 9+). |

For 95% of apps, defaults are correct. Override only when you have a measurement showing the default is wrong.

## Things you control as a developer

You don't write GC heap mappings; you write allocation patterns. The patterns that work well with NUMA-aware Server GC:

### 1. Long-running threads, not transient ones

Server GC's heap affinity assumes a thread stays roughly on one node. Threads that bounce across all CPUs make this guess less effective. Most thread pool workers have moderate affinity already (the OS scheduler maintains it); explicit `Thread` objects you create can be pinned with `SetThreadIdealProcessor` if needed.

### 2. Allocate near the consumer

A common pattern in pipelines: a producer creates objects, a consumer processes them. If producer runs on node 0 and consumer on node 1, the consumer pays remote-memory cost on every dereference. Two fixes:

- **Move the producer near the consumer.** Use one combined thread per pipeline stage on a single node.
- **Use stable thread affinity per pipeline stage.** Either via `SetThreadIdealProcessor` or by sizing the pool such that stages share a node.

### 3. Don't fight LOH on NUMA

The **LOH** (Large Object Heap — where objects ≥85 KB go; not compacted by default to keep allocation cheap) has its own segments. Large arrays first-touch by their constructor; in Server GC each LOH heap has its own segment, but cross-heap traffic during compaction (.NET 5+ added LOH compaction) can pull pages.

For very large arrays you'll re-use, *allocate them on a thread pinned to the consumer's node and keep them alive*. Throwing them away and re-allocating risks placement drift.

### 4. Use POH for pinned interop buffers

(**POH** = Pinned Object Heap — a dedicated heap, introduced in .NET 5, for objects that must never move. Use it for buffers handed to native APIs that need a stable address.)

`GC.AllocateArray<byte>(size, pinned: true)` allocates on the **Pinned Object Heap** — never moves, doesn't need pinning syntax. On NUMA, again, allocate on the consumer's node.

## Measuring GC behaviour

Useful counters:

```
dotnet-counters monitor System.Runtime
  gc-heap-size
  gen-0-gc-count
  gen-1-gc-count
  gen-2-gc-count
  loh-size
  poh-size
  time-in-gc
  alloc-rate
```

GC pause stats:
```
dotnet-trace collect --providers Microsoft-Windows-DotNETRuntime:0x1
# Open in PerfView; the GCStats tab shows per-collection metrics.
```

Hardware-level: `perf c2c` (cache-to-cache traffic) on Linux directly identifies cross-NUMA traffic from your process. Heavy traffic on big shared objects is your smoking gun.

## When to stop tuning

When you've measured the default is fine. Or when you've moved the workload off cross-node patterns entirely. Or when you've concluded GC isn't your bottleneck — most of the time, it isn't.

The single most impactful "NUMA-aware tuning" most apps need is *reducing allocations*, not partitioning them. A workload that allocates 1 GB/s through Gen 0 will be GC-pause-bound; a workload that allocates 10 MB/s won't notice NUMA at all.

## Practical takeaways

- Server GC is the right default for multithreaded apps. Concurrent GC reduces pause length.
- NUMA awareness is largely automatic in .NET 8+. Don't fight it without measurement.
- Allocate near the consumer is a useful rule even within a single-node host (it improves cache locality regardless).
- Reduce allocations first; partition them second.

## Lab

There's no chapter 0 demo for GC-on-NUMA specifically (the variability is too workload-dependent). Chapter 15 ('Performance and Diagnostics') has GC-pause and allocation-rate experiments.

## Further reading

- **Maoni Stephens — `maoni0.medium.com`** — the GC team lead's posts on the design.
- **.NET runtime repo `docs/design/coreclr/jit/*` and `docs/design/coreclr/`** — the canonical internal documentation.
- **Konrad Kokosa — *Pro .NET Memory Management*** — book; long chapter on Server GC.
- **Stephen Toub — *Performance improvements in .NET 8/9*** — sections on GC are the best changelog.
