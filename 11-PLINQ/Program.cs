using System.Diagnostics;
using Concurrency.Shared;

await ConsoleLab.Run("Chapter 11 — PLINQ",
[
    ("Sequential vs PLINQ — primes",   PrimesDemo),
    ("Aggregate — parallel reduction", AggregateDemo),
    ("Ordering cost",                  OrderingDemo),
],
args);

static Task PrimesDemo()
{
    var sw = Stopwatch.StartNew();
    var seqCount = Enumerable.Range(1, 200_000).Count(IsPrime);
    sw.Stop();
    Console.WriteLine($"  sequential: {sw.ElapsedMilliseconds} ms  primes={seqCount}");

    sw.Restart();
    var parCount = Enumerable.Range(1, 200_000).AsParallel().Count(IsPrime);
    sw.Stop();
    Console.WriteLine($"  PLINQ:      {sw.ElapsedMilliseconds} ms  primes={parCount}");
    return Task.CompletedTask;
}

static Task AggregateDemo()
{
    var data = Enumerable.Range(0, 1_000_000).ToArray();

    // Aggregate(seed, accumulator, combine, resultSelector)
    var sum = data.AsParallel().Aggregate(
        seed: 0L,
        func: (acc, x) => acc + x,
        resultSelector: x => x);
    Console.WriteLine($"  Aggregate sum:                {sum}");

    // The four-arg overload with localInit (preferred for performance):
    var sum2 = data.AsParallel().Aggregate(
        seedFactory: () => 0L,                              // per-partition seed
        updateAccumulatorFunc: (acc, x) => acc + x,         // body
        combineAccumulatorsFunc: (a, b) => a + b,           // combine partitions
        resultSelector: x => x);
    Console.WriteLine($"  Aggregate (per-partition seed): {sum2}");
    return Task.CompletedTask;
}

static Task OrderingDemo()
{
    var data = Enumerable.Range(0, 1_000_000).ToArray();

    var sw = Stopwatch.StartNew();
    var unordered = data.AsParallel().Where(x => x % 2 == 0).Sum(x => (long)x);
    sw.Stop();
    Console.WriteLine($"  unordered:  {sw.ElapsedMilliseconds} ms  sum={unordered}");

    sw.Restart();
    var ordered = data.AsParallel().AsOrdered().Where(x => x % 2 == 0).Sum(x => (long)x);
    sw.Stop();
    Console.WriteLine($"  ordered:    {sw.ElapsedMilliseconds} ms  sum={ordered}");
    return Task.CompletedTask;
}

static bool IsPrime(int n)
{
    if (n < 2) return false;
    for (var i = 2; (long)i * i <= n; i++) if (n % i == 0) return false;
    return true;
}
