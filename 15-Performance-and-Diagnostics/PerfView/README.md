# PerfView

Windows-only, free, and unmatched for managed-code performance investigation. PerfView reads ETW traces and presents them with .NET-aware analyses (managed call stacks, GC stats, JIT events, thread pool events, allocation tracks).

## Install

```
https://github.com/microsoft/perfview/releases  → PerfView.exe
```

No installer. Just an executable.

## Quick collection

1. Launch PerfView.
2. **Collect → Collect** (or Alt+C).
3. Default profile is fine for first investigation. Click **Start Collection**.
4. Reproduce the issue.
5. **Stop Collection**.

You get an `.etl` file with kernel + managed events.

## Views you'll use most

- **CPU Stacks**: who's burning CPU. Group by process / module / call tree.
- **GC Stats**: Gen 0/1/2 frequencies, pause times, allocation rate.
- **Thread Time Stacks**: per-thread *wall time* — including blocked time. Critical for finding where threads wait.
- **Wall Time Stacks**: same as above grouped differently.
- **Any Stacks**: any sampled stack trace, by event source.

## Common patterns to find

### Lock contention

Wall Time Stacks → group by stacks containing `Monitor.Enter`. The aggregated time tells you what's contended and for how long.

### ThreadPool starvation

GC Stats → look for "ThreadPoolWorkerThreadAdjustmentAdjustment" events. Frequency + reason ("ThreadAdjustmentReason_ChangePoint") shows hill-climbing decisions. Combine with Thread Time Stacks: many threads blocked in `WaitOne`/`SemaphoreSlim.Wait` indicates starvation.

### Excess allocation

Memory → ETW Heap Snapshot or .NET Allocation Tick. Filter by allocation site; the highest-bytes-allocated method is your suspect.

### Async hot paths

Filter Any Stacks by `Microsoft-Windows-DotNETRuntime/Type/AwaitOnCompleted`. Aggregated counts per method show where async machinery fires most.

## Tips

- **Capture under load**, not idle. Idle profiles show nothing.
- **30 seconds is usually enough**. ETL files are huge (hundreds of MB).
- **Symbol resolution** matters; PerfView uses Microsoft Symbol Server by default. Cache locally to speed up subsequent loads.
- **GroupPats** are powerful — see PerfView's tutorial. They let you collapse "framework code" so you see only your stacks.

## Cross-platform alternative

For Linux/macOS, use `dotnet-trace` + Speedscope or PerfView itself (it can read `.nettrace` files from `dotnet-trace`).

```bash
dotnet-trace collect --process-id <pid> --duration 00:00:30
# then open the .nettrace in PerfView (yes, on Windows is fine; the file is portable)
```
