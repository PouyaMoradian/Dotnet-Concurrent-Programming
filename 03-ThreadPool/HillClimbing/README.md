# Hill climbing

The "hill-climbing" controller decides when the ThreadPool should grow or shrink. It's an empirical optimiser: given recent throughput, it perturbs the worker count and observes whether throughput improved.

## Algorithm summary

Every ~500 ms (configurable):

1. Sample the **work-item completion rate** for the previous interval.
2. Compute the **moving correlation** between recent perturbations and throughput changes.
3. If the correlation is positive, take another step in the same direction.
4. If negative, step back.
5. Add a small random component to escape plateaus.

The constants come from Java's earlier work on adaptive thread pools, blended with .NET-specific tweaks. The full implementation is in `src/libraries/System.Private.CoreLib/src/System/Threading/PortableThreadPool.HillClimbing.cs` in the dotnet/runtime repo.

## Why this matters operationally

- **Bursty load** is the worst case. The first burst sees the steady-state count; the controller takes a few seconds to ramp up. Latency spikes during the ramp.
- **Bimodal workloads** confuse it. If 80% of items take 1 ms and 20% take 1 s, the controller's signal is noisy.
- **Synchronous-over-async** patterns ([18/SyncOverAsync](../../18-Pitfalls-and-Anti-Patterns/SyncOverAsync)) make blocked workers look like *normal* throughput dropping, which causes the controller to add threads — masking the bug while paying for thousands of stuck threads.

## Tuning levers

| Lever | Effect |
|---|---|
| `ThreadPool.SetMinThreads(N, N)` | Bypasses the controller for the first N threads |
| `DOTNET_ThreadPool_HillClimbing_WavePeriod` | Length of the perturbation cycle |
| `DOTNET_ThreadPool_HillClimbing_TargetSignalToNoiseRatio` | How strong correlation must be to trigger a step |

For a server with known concurrency, **set Min to your steady-state worker count**. The pool starts ready and the controller only matters under unusual load.

## Observing it

```bash
dotnet-trace collect --providers Microsoft-Windows-DotNETRuntime:0x10000:5
```

The relevant events are `ThreadPoolWorkerThreadAdjustmentStats`, `ThreadPoolWorkerThreadAdjustmentSample`, `ThreadPoolWorkerThreadAdjustmentAdjustment`. PerfView shows them as a time-series chart; you can literally watch hill-climbing decide.
