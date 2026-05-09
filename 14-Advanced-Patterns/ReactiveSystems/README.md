# Reactive systems

A *reactive* system is event-driven, push-based, with explicit attention to backpressure, error propagation, and isolation. .NET options:

| Library | Style |
|---|---|
| `System.Reactive` (Rx.NET) | Observables; LINQ-shaped operators |
| `IAsyncEnumerable<T>` + `System.Linq.Async` | Pull-based, `await foreach` |
| `Channel<T>` + handcrafted | Most flexible |
| TPL Dataflow | Graph-shaped reactive |

## Push vs pull

- **Push** (Rx): producer drives. Hot streams, mouse events, ticking sensors.
- **Pull** (`IAsyncEnumerable<T>`): consumer drives. Pagination, backpressured streams.

Mixing the two is a common source of bugs. Pick one per pipeline; convert at boundaries with `ToAsyncEnumerable()` / `Observable.FromAsync(...)`.

## Rx in three operators

```csharp
var ticks = Observable.Interval(TimeSpan.FromSeconds(1));

ticks.Where(x => x % 2 == 0)
     .Select(x => $"even tick {x}")
     .Throttle(TimeSpan.FromSeconds(2))
     .Subscribe(Console.WriteLine);
```

`Subscribe` returns an `IDisposable` — disposing it stops the subscription. Forgetting that is the #1 Rx leak.

## Backpressure in Rx

Rx is push: a fast producer can overwhelm a slow consumer. Operators to manage:

- `Buffer(timeSpan, count)` — accumulate.
- `Sample(timeSpan)` — keep latest only at sample interval.
- `Throttle` — emit after quiet period.
- `Window` — like Buffer but emits sub-observables.

For real backpressure (slow producer when consumer slow), Rx isn't the right tool — use channels.

## Reactive principles (Reactive Manifesto, condensed)

- **Responsive**: bounded latency.
- **Resilient**: failure isolated; recovers gracefully (bulkheads, circuit breakers).
- **Elastic**: scales out/in with load.
- **Message-driven**: async, non-blocking, location-transparent.

In .NET, "message-driven" = channels / actors / Dataflow / Rx. The other three are operational concerns.

## When NOT to use Rx

- **You only need a channel-style pipeline.** Rx is heavier than `Channel<T>`.
- **Allocation-sensitive hot paths.** Rx allocates per-operator state.
- **Async iteration is more natural.** `IAsyncEnumerable<T>` is often clearer than the equivalent Observable chain.

## When Rx is the right answer

- **Real-time UI** (WPF/WinForms/Avalonia): mouse/keyboard streams.
- **Composition of many event sources**: `Observable.Merge`, `Observable.CombineLatest`, etc.
- **Schedulers**: explicit control of timing/scheduling via `IScheduler`.

For server-side ASP.NET Core, prefer channels and `IAsyncEnumerable<T>`. For desktop / device telemetry, Rx earns its keep.
