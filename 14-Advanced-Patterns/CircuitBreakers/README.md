# Circuit breakers

When a dependency starts failing, retrying makes it worse. A *circuit breaker* observes a window of recent calls; if the failure rate exceeds a threshold, it **opens** — subsequent calls fail immediately without touching the dependency. After a cooldown, it transitions to **half-open** and lets a test call through; success closes it, failure re-opens.

## States

```
                    failure rate exceeded threshold
       Closed  ─────────────────────────────────►  Open
         ▲                                          │
         │                                          │  cooldown elapsed
         │ test call succeeded                      │
         └──────────  Half-open  ◄──────────────────┘
                          │
                          │ test call failed
                          └──────────► Open
```

## Polly v8

```csharp
var pipe = new ResiliencePipelineBuilder()
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio        = 0.5,        // 50% of calls failing
        MinimumThroughput   = 10,         // require at least 10 calls in window
        SamplingDuration    = TimeSpan.FromSeconds(30),
        BreakDuration       = TimeSpan.FromSeconds(15),
        ShouldHandle        = new PredicateBuilder().Handle<HttpRequestException>(),
    })
    .Build();

await pipe.ExecuteAsync(async ct => await Dependency.CallAsync(ct), ct);
```

When open, `ExecuteAsync` throws `BrokenCircuitException` instantly. Useful for tail-latency: instead of waiting for a 30-second timeout per call during an outage, you fail in microseconds.

## Sizing the parameters

| Parameter | Pick based on |
|---|---|
| `FailureRatio` | what "broken" means for your dependency. 50% is a strong signal; 20% is sensitive. |
| `MinimumThroughput` | enough samples to be statistically meaningful. < 10 → noisy. |
| `SamplingDuration` | rolling window. Match your alerting window. |
| `BreakDuration` | how long to give the dependency to recover. 15-30s is typical. |

## Where to install

**At the boundary to each external dependency**. Per HTTP backend, per database, per cache, per queue. Don't share a circuit breaker across unrelated dependencies — one's outage shouldn't break another's circuit.

In ASP.NET Core, register typed `HttpClient`s with their resilience pipelines:

```csharp
services.AddHttpClient<MyClient>()
        .AddResilienceHandler("default", b => b.AddCircuitBreaker(...));
```

## Combining with retries

```csharp
var pipe = new ResiliencePipelineBuilder()
    .AddTimeout(TimeSpan.FromSeconds(5))                  // outermost: per-call timeout
    .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 })
    .AddCircuitBreaker(...)                                // innermost: counts attempts
    .Build();
```

Order matters. Each layer wraps the next. A retry triggers more attempts; the circuit breaker counts them all. If the circuit is open, retries are short-circuited too.

## Anti-patterns

- **Hand-rolling a circuit breaker** — almost certainly buggier than Polly.
- **One circuit for the whole app** — masks per-dependency outages.
- **Forgetting to handle `BrokenCircuitException`** — your code should have a fallback (cached response, graceful error message).
- **Circuit breaker without bulkhead** — you fail fast but your worker threads still pile up waiting for the bulkhead. Stack them.

## When NOT to circuit-break

- **Idempotent reads with cheap fallback**: just cache and serve stale.
- **One-off scripts**: outages don't repeat in the lifetime of the script.
- **Critical writes**: you may prefer to *retry forever* with exponential backoff than to short-circuit.
