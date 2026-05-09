# 18 — Pitfalls and Anti-Patterns

> **Layer:** all of them
> **Reading time:** ~25 minutes

This chapter is the catalog of failure modes. Every entry is a mistake we've seen ship to production. Each subfolder explains the pattern, why it happens, and how to fix it.

## The hall of fame

| Folder | Failure mode |
|---|---|
| [Deadlocks](Deadlocks/) | Threads waiting for each other; the system never recovers |
| [ThreadPoolStarvation](ThreadPoolStarvation/) | Pool full of blocked workers; throughput collapses |
| [SyncOverAsync](SyncOverAsync/) | `.Result` / `.Wait()` causing deadlock or starvation |
| [HiddenAllocations](HiddenAllocations/) | Async, LINQ, and string interpolation allocating where you don't expect |
| [FalseSharing](FalseSharing/) | Two threads writing to the same cache line; throughput halves |
| [AsyncVoid](AsyncVoid/) | Unobservable exceptions, unhandled, process-killing |
| [OverParallelization](OverParallelization/) | More parallel ≠ faster; sometimes much slower |

## Quick triage

If your service is misbehaving:

| Symptom | Likely chapter |
|---|---|
| Hung; threads alive but no progress | [Deadlocks](Deadlocks/), [SyncOverAsync](SyncOverAsync/) |
| Latency P99 climbs while CPU is low | [ThreadPoolStarvation](ThreadPoolStarvation/) |
| GC runs constantly, allocator-heavy | [HiddenAllocations](HiddenAllocations/) |
| CPU pinned but throughput low | [FalseSharing](FalseSharing/), [OverParallelization](OverParallelization/) |
| Process crashes with unhandled exception from "nowhere" | [AsyncVoid](AsyncVoid/) |

## Run

```bash
dotnet run --project 18-Pitfalls-and-Anti-Patterns
```

The demos *deliberately* reproduce the bugs. Watch the symptoms; learn the shapes.
