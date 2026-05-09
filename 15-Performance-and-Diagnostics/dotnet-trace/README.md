# dotnet-trace

The cross-platform sampled tracer. Cuts through "is the process CPU-bound?" in 30 seconds.

## Install

```bash
dotnet tool install -g dotnet-trace
```

## Standard collection

```bash
# 30 seconds of all default providers
dotnet-trace collect --process-id $(pidof MyApp) --duration 00:00:30

# specific providers (concurrency relevant)
dotnet-trace collect --process-id $(pidof MyApp) \
  --providers Microsoft-Windows-DotNETRuntime:0x10000:5 \
  --duration 00:00:30

# attach to launched process
dotnet-trace collect --diagnostic-port mytool.sock --providers ... \
  -- dotnet run --project MyApp
```

The output is a `.nettrace` file. Open it in:

- **PerfView** (Windows) — most powerful.
- **Visual Studio's "Open Trace File"** — clean UI; less powerful than PerfView.
- **speedscope.app** (browser) — flame graphs, free, after `dotnet-trace convert --format Speedscope`.

## Useful provider keywords (concurrency)

| Keyword | What you get |
|---|---|
| `0x10000` | Threading events (thread inject/retire, hill-climbing) |
| `0x4000` | Contention (Monitor.Enter/Exit, lock contention) |
| `0x1` | GC events |
| `0x80000` | JIT events |
| `0x8000` | Async-IO completion |

Combine with `|` (bitwise OR), e.g., `Microsoft-Windows-DotNETRuntime:0x14001:5` enables threading + contention + GC.

## Custom EventSource

Collect events from your own provider:

```bash
dotnet-trace collect --providers DotnetConcurrency-Demo:0xFFFFFFFFFFFFFFFF:5 --process-id <pid>
```

The `0xFFFF...` keyword mask is "all keywords"; `5` is the verbose level. See [EventPipe](../EventPipe/) for what these mean.

## Convert formats

```bash
dotnet-trace convert mytrace.nettrace --format Chromium       # Chrome DevTools / Perfetto
dotnet-trace convert mytrace.nettrace --format Speedscope     # speedscope.app
```

Speedscope's flamegraph view is fantastic for first-look CPU analysis.

## Common workflows

### "Where's the CPU going?"

```bash
dotnet-trace collect --process-id <pid> --duration 00:00:30
dotnet-trace convert <file>.nettrace --format Speedscope
# open the resulting JSON at https://speedscope.app
```

### "What's the thread pool doing?"

```bash
dotnet-trace collect --process-id <pid> \
  --providers Microsoft-Windows-DotNETRuntime:0x10000:5 \
  --duration 00:00:30
# open in PerfView; filter Any Stacks → ThreadPool*
```

### "Allocation hotspots?"

```bash
dotnet-trace collect --process-id <pid> \
  --providers Microsoft-Windows-DotNETRuntime:0x1:5 \
  --duration 00:00:30
# open in PerfView; GC Heap Net Mem
```

## Limitations

- **Sampling overhead**: ~1-3% CPU. Negligible.
- **Sample resolution**: ~1 ms (configurable). Sub-millisecond methods may not appear.
- **Production**: safe to run, but the file size grows fast — bound the duration.

## When it isn't enough

For deadlock investigation, allocation root analysis, or full kernel correlation, you want **PerfView** (Windows) or `dotnet-dump` + WinDbg / `lldb`.
