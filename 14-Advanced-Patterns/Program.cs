using Concurrency.Shared;
using System.Threading.RateLimiting;
using Polly;
using Polly.CircuitBreaker;

await ConsoleLab.Run("Chapter 14 — Advanced Patterns",
[
    ("Bulkhead — isolating concurrency", BulkheadDemo),
    ("Circuit breaker (Polly)",          CircuitBreakerDemo),
    ("Rate limiter — token bucket",      RateLimiterDemo),
],
args);

static async Task BulkheadDemo()
{
    var orders = new SemaphoreSlim(8);    // bulkhead 1: 8 concurrent order calls
    var inventory = new SemaphoreSlim(2); // bulkhead 2: 2 concurrent inventory calls
    var ok = 0;

    await Parallel.ForEachAsync(Enumerable.Range(0, 20), async (i, _) =>
    {
        await orders.WaitAsync();
        try
        {
            await inventory.WaitAsync();
            try { await Task.Delay(50); Interlocked.Increment(ref ok); }
            finally { inventory.Release(); }
        }
        finally { orders.Release(); }
    });
    Console.WriteLine($"  completed {ok}/20 with bulkheads");
}

static async Task CircuitBreakerDemo()
{
    var pipe = new ResiliencePipelineBuilder()
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 4,
            SamplingDuration = TimeSpan.FromSeconds(5),
            BreakDuration = TimeSpan.FromSeconds(2),
        })
        .Build();

    var attempts = 0;
    var opened = 0;
    for (var i = 0; i < 8; i++)
    {
        try
        {
            await pipe.ExecuteAsync(async _ =>
            {
                attempts++;
                if (attempts <= 4) throw new InvalidOperationException("dependency down");
                await Task.Delay(1);
            });
        }
        catch (BrokenCircuitException) { opened++; }
        catch (InvalidOperationException) { /* counted */ }
    }
    Console.WriteLine($"  attempts: {attempts}  short-circuited: {opened}");
}

static async Task RateLimiterDemo()
{
    var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 5,
        TokensPerPeriod = 5,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        QueueLimit = 20,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    });

    var sw = System.Diagnostics.Stopwatch.StartNew();
    await Parallel.ForEachAsync(Enumerable.Range(0, 15), async (i, ct) =>
    {
        using var lease = await limiter.AcquireAsync(1, ct);
        // do work…
    });
    sw.Stop();
    Console.WriteLine($"  15 ops with 5/sec rate limit took {sw.ElapsedMilliseconds} ms");
}
