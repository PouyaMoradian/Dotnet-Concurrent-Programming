using System.Collections.Frozen;
using System.Threading.RateLimiting;
using Concurrency.Shared;

await ConsoleLab.Run("Chapter 16 — Modern .NET Features",
[
    ("RateLimiting — token bucket",  TokenBucketDemo),
    ("TimeProvider — virtual time",  TimeProviderDemo),
    ("FrozenDictionary read perf",   FrozenDemo),
    ("Task.WhenEach (.NET 9+)",      WhenEachDemo),
],
args);

static async Task TokenBucketDemo()
{
    var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 5,
        TokensPerPeriod = 5,
        ReplenishmentPeriod = TimeSpan.FromMilliseconds(500),
        QueueLimit = 100,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    });

    var sw = System.Diagnostics.Stopwatch.StartNew();
    for (var i = 0; i < 12; i++)
    {
        using var lease = await limiter.AcquireAsync(1);
        Console.WriteLine($"  +{sw.ElapsedMilliseconds,4} ms  acquired #{i}  (success={lease.IsAcquired})");
    }
}

static Task TimeProviderDemo()
{
    var fake = new FakeTimeProvider();
    Console.WriteLine($"  fake initial:  {fake.GetUtcNow():u}");
    fake.Advance(TimeSpan.FromMinutes(5));
    Console.WriteLine($"  after Advance: {fake.GetUtcNow():u}");
    Console.WriteLine();
    Console.WriteLine("  In tests, inject TimeProvider so DateTime.UtcNow / Task.Delay become controllable.");
    return Task.CompletedTask;
}

static Task FrozenDemo()
{
    var sample = Enumerable.Range(0, 1000).ToDictionary(i => $"key_{i}", i => i);
    var frozen = sample.ToFrozenDictionary();
    Console.WriteLine($"  built FrozenDictionary with {frozen.Count} entries");
    Console.WriteLine($"  reads are typically 2-10× faster than Dictionary, with no concurrency cost.");
    return Task.CompletedTask;
}

static async Task WhenEachDemo()
{
    Task<int>[] tasks =
    [
        Delay(300, 1), Delay(100, 2), Delay(200, 3),
    ];

    await foreach (var t in Task.WhenEach(tasks))
    {
        Console.WriteLine($"   completed in order: {await t}");
    }
}

static async Task<int> Delay(int ms, int v) { await Task.Delay(ms); return v; }

internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan d) => _now += d;
}
