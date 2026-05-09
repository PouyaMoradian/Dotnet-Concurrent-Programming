# Rate limiting

Bound *operations per unit time*, not just concurrency. .NET 8 ships `System.Threading.RateLimiting` — a first-class API.

See [16-Modern-.NET-Features/RateLimiting](../../16-Modern-.NET-Features/RateLimiting/) for the full deep dive. Quick summary:

| Limiter | Algorithm |
|---|---|
| `ConcurrencyLimiter` | bulkhead — at most N concurrent |
| `FixedWindowRateLimiter` | N permits per fixed window |
| `SlidingWindowRateLimiter` | N permits per rolling window (smoother) |
| `TokenBucketRateLimiter` | bucket fills at rate R, capacity C |

## Choosing

- **Bursty + smooth** → `TokenBucketRateLimiter` (preferred default).
- **Strict per-window** (e.g., 1000 calls/minute) → `FixedWindowRateLimiter`.
- **Concurrency only** → `ConcurrencyLimiter`.

## Per-key partitioning

Real systems rate-limit *per tenant* / per API key:

```csharp
var partitioner = PartitionedRateLimiter.Create<HttpContext, string>(httpCtx =>
{
    var apiKey = httpCtx.Request.Headers["X-API-Key"].ToString();
    return RateLimitPartition.GetTokenBucketLimiter(apiKey, _ => new TokenBucketRateLimiterOptions
    {
        TokenLimit = 100, TokensPerPeriod = 100, ReplenishmentPeriod = TimeSpan.FromSeconds(1),
    });
});
```

Each tenant has its own bucket. ASP.NET Core's `app.UseRateLimiter()` middleware integrates this directly.

## Composing with bulkhead and circuit breaker

Resilience stack from outermost to innermost:

```
  HTTP middleware
     │
     ▼
  Rate Limiter   ← reject excess at boundary; fast 429
     │
     ▼
  Timeout        ← per-call deadline
     │
     ▼
  Retry          ← idempotent only
     │
     ▼
  Circuit Breaker ← fail fast on dependency illness
     │
     ▼
  Bulkhead       ← concurrent cap on dependency
     │
     ▼
  Dependency
```

Each layer has a different reason to reject; rejection at any layer should be observable in metrics.

## Anti-patterns

- **Rate limit *output* without considering *input***: if you accept at 10k/sec but limit downstream at 1k/sec, the queue grows. Limit at the boundary (input).
- **Per-process rate limiter for a multi-instance service**: each instance enforces independently. The total system rate is N×limit. Either coordinate via Redis (`StackExchange.Redis` + Lua), or set per-instance limit = global ÷ N.
- **Failing silently when rate-limited**: emit a metric (`rejected_total`); return 429 with `Retry-After` to clients.
