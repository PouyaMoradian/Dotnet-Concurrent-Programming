# API gateways

A gateway is the boundary between the public internet and your internal services. It must:

- Authenticate.
- Rate-limit per tenant.
- Apply circuit breakers and bulkheads to each downstream.
- Cache.
- Translate / aggregate.
- Expose telemetry.

In .NET, you'd build it on **ASP.NET Core** (or use **YARP**, the production-grade reverse-proxy library).

## Concurrency-relevant components

```csharp
var builder = WebApplication.CreateBuilder(args);

// Per-tenant rate limit (token bucket).
builder.Services.AddRateLimiter(o =>
{
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
    {
        var tenant = http.Request.Headers["X-Tenant"].ToString();
        return RateLimitPartition.GetTokenBucketLimiter(tenant, _ => new()
        {
            TokenLimit = 100, TokensPerPeriod = 100, ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        });
    });
    o.RejectionStatusCode = 429;
});

// Typed HttpClient with resilience pipeline.
builder.Services.AddHttpClient<UpstreamClient>(c =>
{
    c.BaseAddress = new("https://upstream.example");
    c.Timeout = TimeSpan.FromSeconds(5);
})
.AddResilienceHandler("upstream", b =>
{
    b.AddTimeout(TimeSpan.FromSeconds(3));
    b.AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 2 });
    b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5,
        MinimumThroughput = 10,
        SamplingDuration = TimeSpan.FromSeconds(30),
        BreakDuration = TimeSpan.FromSeconds(15),
    });
});

var app = builder.Build();
app.UseRateLimiter();

app.MapGet("/api/data", async (UpstreamClient up, CancellationToken ct) =>
{
    var data = await up.GetAsync(ct);
    return Results.Ok(data);
});

app.Run();
```

## Concurrency profile

- **Each request runs on a pool worker** until first await; resumes wherever the awaiter completes.
- **`AsyncLocal<T>`** flows context (auth principal, correlation id, OTel activity) automatically.
- **Backpressure**: the rate limiter rejects upstream of work; bulkheads (in `AddResilienceHandler`) cap concurrent downstream calls; circuit breakers fail fast on dependency failure.

## What the gateway must NOT do

- **Block on `.Result`** — async-end-to-end, top to bottom.
- **Allocate per-request large buffers** without `ArrayPool<T>`.
- **Construct `HttpClient`s per request** — one typed client per dependency (DI gives you this).
- **Hold per-request state in static fields** — use `AsyncLocal<T>` or pass explicitly.

## Tail-latency hygiene

In a gateway, a single slow downstream causes a fan-out delay for every request that touches it. Defenses:

1. **Hedging**: send a second request after a delay, take whichever returns first. Polly v8 has a strategy for this.
2. **Per-downstream timeouts** that are tighter than the user-facing timeout.
3. **Circuit breaker** to fast-fail when a downstream is dead.
4. **Bulkhead** to prevent one slow dependency from saturating workers.

## Observability essentials

- Per-route latency histograms.
- Per-downstream error rates.
- Per-tenant rate-limit rejection counts.
- Active connections, in-flight requests.

In OpenTelemetry-instrumented services, this is mostly free — `app.UseRouting()` + `AddOpenTelemetry()`.
