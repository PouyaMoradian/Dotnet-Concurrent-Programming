# Unbounded channels

`Channel.CreateUnbounded<T>()` allocates segments dynamically, so writers never block on capacity. Reads still block when the buffer is empty. **They are dangerous unless something else is bounding the producer.**

## When unbounded is correct

- The **producer is rate-limited** by something other than the channel (e.g., an HTTP API that already throttles itself).
- The producer rate is *known* to be bounded for the lifetime of the process (a small file's lines, a finite event source).
- The channel is short-lived (per-request fan-in/fan-out).

## When unbounded is a leak

The classic disaster:

```csharp
var ch = Channel.CreateUnbounded<Telemetry>();   // ❌ no backpressure
ProcessIncomingMetrics(ch.Writer);                // produces at 100k/s
DeliverToBackend(ch.Reader);                      // consumes at 10k/s when healthy

// backend goes slow → channel grows without bound → OOM
```

Memory grows; GC churns; the OOM happens an hour later. The fix is bounded with appropriate `FullMode`:

```csharp
var ch = Channel.CreateBounded<Telemetry>(new BoundedChannelOptions(10_000)
{
    FullMode = BoundedChannelFullMode.DropOldest,   // prefer dropping old data when overloaded
});
```

## Implementation note

Unbounded channels use a linked list of segments. Each segment is a small array; segments are added when the current one fills. `WriteAsync` is essentially synchronous (returns a completed task) — the only "async" cost is the rare segment allocation.

## Reasonable use case: per-actor mailbox

When a channel's lifetime is the same as an actor's, and the actor's protocol guarantees finite messages per call site, unbounded is reasonable:

```csharp
public sealed class Actor
{
    private readonly Channel<Command> _mb = Channel.CreateUnbounded<Command>(); // OK — actor processes serially

    public ValueTask Send(Command c) => _mb.Writer.WriteAsync(c);
    public Task RunAsync(CancellationToken ct) => Task.Run(async () =>
    {
        await foreach (var c in _mb.Reader.ReadAllAsync(ct)) Handle(c);
    }, ct);
}
```

But add a bound the moment you can't argue from first principles that the producer rate is finite.

## Telemetry counter

For production: emit `channel-queue-length` to your metrics. If it consistently grows, you've found unbounded behavior masquerading as bounded.
