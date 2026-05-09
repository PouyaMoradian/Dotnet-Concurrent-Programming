# ETW (Event Tracing for Windows)

Windows-only. The kernel-level event mechanism behind PerfView. Provides:

- **Kernel events** — context switches, page faults, DPCs, IO completions.
- **CLR events** — JIT, GC, ThreadPool, contention.
- **User-mode events** — your `EventSource`s.

EventPipe (cross-platform) is the .NET-process-only subset. ETW gives you everything *and* the kernel — which is necessary for context switches, IO completion, and disk activity.

## Tools that consume ETW

- **PerfView** — the .NET-aware UI.
- **WPR (Windows Performance Recorder)** + **WPA (Analyzer)** — broader (UI scenarios, video, etc.).
- **`xperf`** — command-line.
- **logman** — built-in scheduler.

## Quick collection (logman)

```bash
logman create trace MyApp -p Microsoft-Windows-DotNETRuntime 0x10000 5 -o C:\trace.etl
logman start MyApp
# reproduce
logman stop MyApp
```

`-p` provider, keyword mask, level. Output `.etl` opens in PerfView.

## CLR ETW providers

| GUID | Name | What |
|---|---|---|
| `e13c0d23-ccbc-4e12-931b-d9cc2eee27e4` | Microsoft-Windows-DotNETRuntime | the runtime — GC, JIT, ThreadPool, etc. |
| `319dc449-ada5-50f7-428e-957db6791668` | Microsoft-Windows-DotNETRuntimePrivate | internal; informational; less stable |

## Concurrency-relevant keyword masks

| Bit | Keyword | Use |
|---|---|---|
| `0x1` | GC | Generational events |
| `0x4000` | Contention | Monitor.Enter/Exit when contended |
| `0x10000` | Threading | ThreadPool worker inject/retire, hill-climbing |
| `0x40000` | ThreadTransfer | Async work continuations between threads |
| `0x80000` | JIT | JIT method compile events |
| `0x100000` | TieredCompilation | Tier-up events |

Combine with bitwise OR: `0x14001` for GC + threading + contention.

## When ETW > EventPipe

- **Kernel-level analysis** (context switches, IO completions). EventPipe is process-only.
- **Multi-process** scenarios — ETW captures everything system-wide; EventPipe attaches to one process.
- **Lock-contention root cause** — `Microsoft-Windows-Kernel-Memory` etc. ties to ETW.

## When you don't need it

If you're working cross-platform or you don't need kernel correlation, EventPipe is enough and works on Linux. ETW is for "I'm on Windows and I need the deep view."

## Performance impact

Kernel ETW + heavy CLR keywords can add 5-15% overhead. Production-safe for short windows; not for sustained-on. EventPipe is < 1%.
