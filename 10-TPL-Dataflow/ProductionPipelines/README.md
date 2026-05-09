# Production pipelines

A realistic shape: ingest, parse, enrich (DB lookups), batch, write to a backend, with full backpressure, error isolation, and graceful shutdown.

```csharp
public sealed class TelemetryPipeline : IAsyncDisposable
{
    private readonly TransformBlock<RawEvent, ParsedEvent> _parse;
    private readonly TransformBlock<ParsedEvent, EnrichedEvent> _enrich;
    private readonly BatchBlock<EnrichedEvent> _batch;
    private readonly ActionBlock<EnrichedEvent[]> _send;

    public TelemetryPipeline(IEnricher enricher, IBackend backend)
    {
        _parse = new(static raw => Parse(raw),
            new() { MaxDegreeOfParallelism = Environment.ProcessorCount, BoundedCapacity = 200 });

        _enrich = new(async p => await enricher.EnrichAsync(p),
            new() { MaxDegreeOfParallelism = 16, BoundedCapacity = 200 });

        _batch = new BatchBlock<EnrichedEvent>(100,
            new() { BoundedCapacity = 500, Greedy = true });

        _send = new(async batch => await backend.SendAsync(batch),
            new() { MaxDegreeOfParallelism = 4, BoundedCapacity = 50 });

        var link = new DataflowLinkOptions { PropagateCompletion = true };
        _parse.LinkTo(_enrich, link);
        _enrich.LinkTo(_batch, link);
        _batch.LinkTo(_send, link);
    }

    public ValueTask SubmitAsync(RawEvent ev) => new(_parse.SendAsync(ev));

    public async ValueTask DisposeAsync()
    {
        _parse.Complete();
        try { await _send.Completion; }
        catch (Exception ex) { /* log; rethrow as needed */ throw; }
    }
}
```

## What's good about this

- **Each stage's parallelism matches its bottleneck.** Parse is CPU-bound → ProcessorCount. Enrich is IO-bound → higher parallelism (16). Batch is glue (no CPU). Send is bandwidth-bound (4 connections).
- **Bounds everywhere.** No stage can grow unboundedly.
- **Graceful shutdown.** `_parse.Complete()` propagates; `await _send.Completion` waits for the last batch.
- **Errors propagate.** Any block fault becomes `_send.Completion` faulting.

## What to add for real production

- **Telemetry per stage**: `_parse.InputCount`, `_enrich.InputCount`, etc. Push to Prometheus/Honeycomb.
- **Per-item tracing**: each event carries a correlation id; OpenTelemetry activity propagated via `AsyncLocal<Activity>`.
- **Dead-letter queue**: a separate block routes failed items rather than killing the pipeline.
- **Rate limiting** per backend connection — combine with `System.Threading.RateLimiting` ([16-Modern](../../16-Modern-.NET-Features/RateLimiting)).
- **Restart policy** if the pipeline faults: tear down, allocate a new one, replay any unsent batches from a WAL.

## Comparison with Channel-based pipelines

The Channel version is more code (you write each stage's worker task) but smaller and faster per item. For very high throughput (10M+ events/sec), measure both. Below that scale, pick on readability — and Dataflow tends to be more readable when stages have different parallelism.

## Real shipped examples

- **OpenTelemetry .NET** uses Channel-based pipelines internally (lower allocation).
- **Application Insights SDK** historically used Dataflow blocks for its telemetry channel.
- **Application-level shipping** code in many internal-tool teams uses Dataflow for its per-stage tunability.
