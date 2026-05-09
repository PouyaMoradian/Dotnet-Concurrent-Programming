# Bulkheads

A bulkhead isolates failure: one slow / failing subsystem shouldn't take down the whole service. The Titanic analogy: water in one compartment doesn't sink the ship.

## Implementation

A bulkhead is just a *concurrency cap* dedicated to one dependency:

```csharp
public sealed class DependencyClient
{
    private readonly SemaphoreSlim _bulkhead;
    private readonly HttpClient _http;

    public DependencyClient(int maxConcurrency, HttpClient http)
    {
        _bulkhead = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _http = http;
    }

    public async Task<T> CallAsync<T>(string path, CancellationToken ct)
    {
        if (!await _bulkhead.WaitAsync(TimeSpan.FromMilliseconds(50), ct))
            throw new BulkheadRejectedException();      // dependency is saturated; fail fast

        try { return await _http.GetFromJsonAsync<T>(path, ct) ?? throw new InvalidDataException(); }
        finally { _bulkhead.Release(); }
    }
}
```

The bulkhead bounds calls *into* the dependency. When the dependency slows, the bulkhead saturates and rejects further calls *fast* — instead of all your worker threads piling up waiting.

## Sizing

A bulkhead's capacity = the **maximum healthy concurrency** the dependency expects. For an HTTP backend that scales linearly to 50 concurrent requests, set the bulkhead to ~50. Past that, you're causing the dependency's tail latency.

If you don't know, start at `2 * connection_pool_size` and tune based on metrics.

## Two-tier bulkheading

In high-stakes systems, isolate per *priority*:

- 80% of bulkhead capacity for "free traffic."
- 20% reserved for "critical" calls (admin, paid tier, health checks).

Implement as two bulkheads sharing the same dependency, or as a `RateLimiter` with partitioned keys.

## Polly's bulkhead strategy

```csharp
var pipe = new ResiliencePipelineBuilder()
    .AddRateLimiter(new ConcurrencyLimiter(new ConcurrencyLimiterOptions
    {
        PermitLimit = 50,
        QueueLimit = 10
    }))
    .Build();
```

Polly v8 dropped the standalone `BulkheadPolicy` in favour of `ConcurrencyLimiter` from `System.Threading.RateLimiting` — same shape, broader capabilities.

## What a bulkhead is NOT

- **A retry policy.** Retries belong in a separate strategy.
- **A circuit breaker.** Circuit breakers stop calls based on observed failures; bulkheads stop based on observed concurrency.
- **A rate limiter.** Bulkheads cap concurrency; rate limiters cap operations per time.

The trio (bulkhead + circuit breaker + retry) is the common resilience stack — and Polly composes them.

## Telemetry

Emit per bulkhead:

- `acquired_total`, `rejected_total`
- `current_in_flight`
- `wait_time_ms` histogram

If you see consistent rejections, your dependency is overloaded — *or* your bulkhead is too tight.
