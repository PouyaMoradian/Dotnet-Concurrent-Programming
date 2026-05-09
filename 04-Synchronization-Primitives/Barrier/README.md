# Barrier

A `Barrier` synchronises N threads at *phase boundaries*. All N threads call `SignalAndWait()`; once the Nth signals, they all proceed to the next phase, optionally running a *post-phase action*.

## Canonical use: iterative parallel computation

A simulation step computes new state from the previous step. You can't start step `k+1` until *every* worker has finished step `k`.

```csharp
var workers = 8;
using var barrier = new Barrier(workers, b =>
{
    // post-phase: aggregate per-worker results, publish next step input
    Aggregate();
});

Parallel.For(0, workers, id =>
{
    for (var phase = 0; phase < TotalPhases; phase++)
    {
        ComputeMyShare(id, phase);
        barrier.SignalAndWait();         // wait for the rest, then post-phase action runs once
    }
});
```

## Vital details

- **Adding/removing participants mid-flight** is supported via `AddParticipant`/`RemoveParticipant`. Useful when a participant exits early.
- **`PostPhaseException`** — if the post-phase action throws, every waiter at the next `SignalAndWait` gets a `BarrierPostPhaseException`. Don't ignore it.
- **`CurrentPhaseNumber`** — increments after each successful phase. Used inside the post-phase action.

## When Barrier is the right tool

- **Phased simulations** (Game of Life, fluid sim, gradient descent steps).
- **Multi-pass image / signal processing** where each pass depends on the previous.

## When it isn't

- **Pipelines** — use `Channel<T>` or TPL Dataflow. A barrier forces every worker to wait on the slowest, which is what you want at phase boundaries but *not* what you want in a streaming pipeline.
- **Fork/join with arbitrary counts** — `Task.WhenAll` is simpler.

## Performance

Barriers are not on a hot path; they're called once per phase. Their cost is the cost of N waiters parking and resuming. For very fine-grained phases (sub-microsecond), the overhead dominates and you should batch multiple "phases" into one.
