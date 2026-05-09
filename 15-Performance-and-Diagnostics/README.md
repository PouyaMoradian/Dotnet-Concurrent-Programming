# 15 — Performance and Diagnostics

> **Layer:** runtime + tooling
> **Reading time:** ~30 minutes
> **Prereq:** any chapter you've measured before

You can't fix what you can't see. This chapter is the toolbox: the profilers, counters, and event sources that let you reason about a running .NET process.

## Tooling map

| Tool | Strengths | Use case |
|---|---|---|
| `BenchmarkDotNet` | Statistically rigorous micro-benchmarks | Measuring a method's perf and allocations |
| `dotnet-counters` | Real-time counters, no-install | First look at a misbehaving process |
| `dotnet-trace` | Sampled stacks; via EventPipe | CPU profiling on Linux/Win |
| `dotnet-dump` | Process dumps for offline analysis | Hangs, deadlocks, OOM forensics |
| `dotnet-gcdump` | GC heap snapshots | Memory leaks |
| **PerfView** | Most powerful Windows ETW + managed analysis | Production-grade investigation |
| **Honeycomb / Datadog / etc.** | Distributed tracing | Multi-service systems |
| **Linux `perf`** | Kernel + user CPU sampling | Hot loops, context switch analysis |
| **`speedscope.app`** | Free flamegraph viewer | Visualising trace files |

## In-chapter folders

| Folder | Topic |
|---|---|
| [PerfView](PerfView/) | Windows ETW + managed analysis; the single best concurrency tool |
| [dotnet-trace](dotnet-trace/) | Cross-platform sampled tracing |
| [dotnet-counters](dotnet-counters/) | Live counter monitoring |
| [EventPipe](EventPipe/) | The transport behind the dotnet-* CLIs |
| [BenchmarkDotNet](BenchmarkDotNet/) | Microbenchmarking tips |
| [FlameGraphs](FlameGraphs/) | Reading flamegraphs |
| [ETW](ETW/) | Windows Event Tracing details |
| [GC-Pressure](GC-Pressure/) | Spotting and fixing allocation pressure |

## A first-aid playbook

When a service is misbehaving:

1. **`dotnet-counters monitor --process-id <pid>`** — look at:
   - `cpu-usage` — is the process CPU-bound?
   - `gen-2-gc-count` — GC fires?
   - `threadpool-thread-count`, `threadpool-queue-length` — pool pressure?
   - `monitor-lock-contention-count` — lock contention?
2. If CPU-bound → `dotnet-trace collect` 30 seconds → load in PerfView / Speedscope → flamegraph → find the hot method.
3. If memory-bound → `dotnet-gcdump collect` → load in PerfView → look for high-count objects.
4. If thread-pool starvation → check for sync-over-async; `dotnet-trace` with the threading event source captures injection events.
5. If hung → `dotnet-dump collect` → `dotnet-dump analyze` → `clrstack -all` to find blocked threads.

## Run

```bash
dotnet run --project 15-Performance-and-Diagnostics
```
