using System.Diagnostics;

namespace Chapter05.Demos;

internal static class CounterContention
{
    public static async Task Run()
    {
        const int threads = 8;
        const int iters = 5_000_000;

        // 1) lock-based.
        var sync = new object();
        long lockedCount = 0;
        var sw = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < iters; i++)
                lock (sync) lockedCount++;
        })));
        sw.Stop();
        Console.WriteLine($"  lock(object)      : {sw.ElapsedMilliseconds,5} ms  ({lockedCount:N0})");

        // 2) Interlocked.
        long atomicCount = 0;
        sw.Restart();
        await Task.WhenAll(Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < iters; i++) Interlocked.Increment(ref atomicCount);
        })));
        sw.Stop();
        Console.WriteLine($"  Interlocked       : {sw.ElapsedMilliseconds,5} ms  ({atomicCount:N0})");

        // 3) Sharded counters.
        var shards = new long[threads * 16];      // pad to avoid false sharing
        sw.Restart();
        await Task.WhenAll(Enumerable.Range(0, threads).Select(t => Task.Run(() =>
        {
            ref var slot = ref shards[t * 16];
            for (var i = 0; i < iters; i++) slot++;
        })));
        sw.Stop();
        long shardedTotal = 0;
        for (var t = 0; t < threads; t++) shardedTotal += shards[t * 16];
        Console.WriteLine($"  sharded counters  : {sw.ElapsedMilliseconds,5} ms  ({shardedTotal:N0})");

        Console.WriteLine();
        Console.WriteLine("  Sharding wins because it removes the cache-line contention entirely.");
        Console.WriteLine("  This is the pattern Parallel.For's localInit/localFinally implements automatically.");
    }
}
