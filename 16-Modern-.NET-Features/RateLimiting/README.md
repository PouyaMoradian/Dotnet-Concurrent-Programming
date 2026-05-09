# `System.Threading.RateLimiting`

The first-class rate-limiting API. Four algorithms, partitioning, async-aware. Available since .NET 7; production-grade in .NET 8.

## The four algorithms

| Limiter | Behaviour | Best for |
|---|---|---|
| `ConcurrencyLimiter` | At most N concurrent permits | Bulkhead / connection pool |
| `FixedWindowRateLimiter` | N permits per fixed window | "1000 calls per minute" strict |
| `SlidingWindowRateLimiter` | N permits per rolling window (sub-windows) | Smoother fixed-window |
| `TokenBucketRateLimiter` | Bucket fills at R, capacity C | Bursty + smooth — the usual default |

## Token bucket — the canonical default

```csharp
var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
{
    TokenLimit          = 100,                    // bucket size
    TokensPerPeriod     = 50,
    ReplenishmentPeriod = TimeSpan.FromSeconds(1), // 50 tokens per second
    QueueLimit          = 200,                     // queue waiting acquirers
    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
});
```

Acquire:

```csharp
using var lease = await limiter.AcquireAsync(permitCount: 1, ct);
if (!lease.IsAcquired) throw new InvalidOperationException("rejected");
// do work
```

`lease` is `IDisposable` — disposing returns the permit (for `ConcurrencyLimiter`; no-op for the rate kind, since rate limits don't return).

## Partitioning (per-tenant)

Real services rate-limit *per* API key / IP / tenant:

```csharp
var partitioner = PartitionedRateLimiter.Create<HttpContext, string>(http =>
{
    var apiKey = http.Request.Headers["X-API-Key"].ToString();
    return RateLimitPartition.GetTokenBucketLimiter(apiKey,
        _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 100, TokensPerPeriod = 100,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        });
});
```

Each tenant has its own independent bucket. The `PartitionedRateLimiter` lazily creates / cleans up sub-limiters per key.

## ASP.NET Core integration

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(...);
    options.RejectionStatusCode = 429;
    options.OnRejected = (ctx, ct) =>
    {
        ctx.HttpContext.Response.Headers.RetryAfter = "10";
        return ValueTask.CompletedTask;
    };
});

app.UseRateLimiter();   // applies the global limiter
```

You can also attach per-endpoint policies via `[EnableRateLimiting("policy-name")]`.

## Distributed rate-limiting (per-cluster)

The built-in limiters are **per-process**. If you have N replicas, each enforces its own limit. Total system rate is N×limit.

For cluster-wide limits:

- Set per-replica limit = global ÷ N (simple; fairness depends on load balancer).
- Use a Redis-backed limiter (e.g., `StackExchange.Redis` + Lua scripts; or [RedisRateLimiting](https://github.com/cristipufu/aspnetcore-redis-rate-limiting)) for true distribution.

## Comparison with hand-rolled

A `SemaphoreSlim` gives you concurrency limit only — no time-based rate. A `Stopwatch`-based hand-rolled token bucket has a long history of off-by-one bugs and interlocking issues. `TokenBucketRateLimiter` is tested, async-aware, and partition-aware.

## Anti-patterns

1. **Limiter created per request.** Defeats the purpose. Long-lived; one per scope (per-tenant via partitioning, per-app for global).
2. **Forgetting to dispose the lease.** For `ConcurrencyLimiter` this leaks a slot.
3. **Using rate-limiter as a circuit breaker.** They reject for different reasons; combine them, don't substitute.
