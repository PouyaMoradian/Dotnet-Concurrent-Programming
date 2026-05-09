# Telemetry pipelines

Collecting metrics, logs, and traces at scale is concurrency-hard. The producer side — every request emitting events — is high-frequency, allocation-sensitive, and *must not block business logic*. The consumer side — batching, compressing, shipping to backend — is bandwidth-limited.

## Architecture

```
   App code
       │  emit()  (must be < 100 ns)
       ▼
   ┌──────────┐    ┌──────────┐    ┌──────────┐
   │  Channel │ →  │  Batcher │ →  │  Exporter│ → backend
   │  bounded │    │ N items  │    │  HTTP    │
   └──────────┘    └──────────┘    └──────────┘
        ▲
        │ DropOldest on overflow
```

## The hot-path emitter

```csharp
public sealed class TelemetryClient
{
    private readonly Channel<TelemetryEvent> _ch = Channel.CreateBounded<TelemetryEvent>(
        new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true, SingleWriter = false,
        });

    public bool TryEmit(TelemetryEvent ev) => _ch.Writer.TryWrite(ev);
}
```

`TryWrite` is non-blocking and *near-lock-free* in MPMC bounded channels. Production code paths emit in tens of nanoseconds. `DropOldest` ensures the application *never* blocks waiting for telemetry to be ingested.

## The batcher

```csharp
private async Task BatchAndShipAsync(CancellationToken ct)
{
    var buf = new List<TelemetryEvent>(capacity: 100);
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

    var reader = _ch.Reader;
    while (await reader.WaitToReadAsync(ct))
    {
        while (reader.TryRead(out var item)) buf.Add(item);

        if (buf.Count >= 100 || (await timer.WaitForNextTickAsync(ct)))
        {
            if (buf.Count > 0)
            {
                try { await ShipAsync(buf, ct); }
                catch (Exception ex) { Log.Error(ex, "ship failed"); }
                buf.Clear();
            }
        }
    }
}
```

Ships when:

- buffer reaches batch size, or
- the timer ticks (so slow days still ship).

## The exporter

Bounded concurrency to the backend (a single `HttpClient` with persistent connections; 4–8 concurrent posts):

```csharp
private static readonly SemaphoreSlim _shipSlot = new(4);

private async Task ShipAsync(List<TelemetryEvent> batch, CancellationToken ct)
{
    await _shipSlot.WaitAsync(ct);
    try
    {
        var content = Serialize(batch);
        var resp = await _http.PostAsync("/ingest", content, ct);
        resp.EnsureSuccessStatusCode();
    }
    finally { _shipSlot.Release(); }
}
```

Polly's circuit breaker + retry around the ship is appropriate.

## Memory shape

A `TelemetryEvent` should be a *struct* (or pre-allocated class) so emit doesn't allocate. The `Channel<T>` of structs holds them inline; no boxing.

```csharp
public readonly struct TelemetryEvent
{
    public readonly long TimestampTicks;
    public readonly int Kind;
    public readonly long Value;
    public readonly string Name;
    public TelemetryEvent(int kind, long value, string name) { … }
}
```

## What can go wrong

- **Unbounded channel** → memory growth on backend slowdown. Always bounded.
- **`Wait` mode** → application blocks emit, latency spikes. `DropOldest` is preferred; report drop counts as a separate metric.
- **Allocation per emit** → high allocator pressure under load. Use structs; consider `ArrayPool` for batch buffers.
- **Ship failures swallowed** → silent data loss. Always log; expose error metrics.

## Realistic scale

A telemetry pipeline of this shape handles tens of millions of events per second per instance with sub-microsecond emit latency. The standard for production observability backends.

## Vendor-built pipelines

OpenTelemetry's collector and most APM SDKs (Honeycomb, Datadog, New Relic) implement this exact shape internally. Worth understanding the architecture before reaching for a custom one — the SDKs handle batching, retry, and graceful shutdown.
