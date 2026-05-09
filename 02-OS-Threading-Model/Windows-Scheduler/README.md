# The Windows scheduler

Windows uses a **32-level priority** preemptive scheduler. Levels are split:

| Range | Class |
|---|---|
| 0 | Zero-page thread (kernel housekeeping) |
| 1–15 | Dynamic priority class — most user threads live here |
| 16–31 | Real-time priority class — preempts dynamic; needs `SeIncreaseBasePriorityPrivilege` |

A thread's priority is `process priority class + thread priority`. .NET maps `ThreadPriority.Normal/Below/AboveNormal` etc. onto Windows base priority offsets.

## Quanta

A *quantum* is the slice of CPU before forced preemption.

| Edition | Default quantum |
|---|---|
| Windows desktop | ~20 ms (3× ~6.6 ms ticks); **foreground app gets a 3× boost** so its threads get longer slices |
| Windows server | ~120 ms; longer = fewer context switches under server load |

You can shorten the OS clock tick with `timeBeginPeriod(1)` — the legacy multimedia API. Doing so makes `Thread.Sleep(1)` actually take ~1 ms instead of ~15 ms but increases system-wide power consumption. **Don't do this in libraries.** A few notable products historically did, and they were rightfully shamed for it.

## Priority boosts

Windows applies several boosts to keep responsiveness high:

| Boost | Trigger |
|---|---|
| GUI thread foreground boost | Window receives focus; +2 priority |
| IO completion boost | Thread completes IO; +1–8 depending on device class |
| Wait-completion boost | Thread blocked on event/semaphore is signalled; small boost |
| Anti-starvation (CPU starvation avoidance) | Ready threads not scheduled for ~3 sec get +15 once |

The IO-completion boost is the reason a server doing async IO behaves "snappily" without you having to tune anything.

## What the .NET dev sees

```csharp
Thread.CurrentThread.Priority = ThreadPriority.AboveNormal; // → +2 in dynamic range
```

Avoid using priorities to fix concurrency bugs. They mask scheduling decisions; they don't fix them. Inversion bugs become harder to diagnose when half the threads are non-default priority.

## Tools

- **Sysinternals Process Explorer** — per-thread CPU%, priority, base priority, context switches/sec.
- **xperf / WPR** — full ETW kernel scheduler trace; see context switches and ready/run reasons.
- **PerfView** — built on TraceEvent; great for managed scheduling correlation.

## Anti-pattern: priority inversion

A high-priority thread blocks waiting for a lock held by a low-priority thread. A medium-priority thread is runnable and starves the lock holder. Result: high-priority thread waits indefinitely. Windows mitigates with **boost on wait acquisition** but the architectural fix is to not introduce the inversion.
