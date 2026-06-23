using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Chapter00.Demos;

/// <summary>
/// Shows the cost of contention on a single cache line. Many threads incrementing the
/// same <c>Interlocked.Increment</c> serialise through MESI: each RFO is a round trip
/// on the interconnect. Replacing the single counter with one-per-thread (padded to
/// avoid false sharing) eliminates the cross-core traffic and restores near-linear
/// scaling. Same number of total increments; only the layout differs.
/// </summary>
internal static class ContendedInterlockedDemo
{
    public static async Task Run()
    {
        var threads = Math.Min(Environment.ProcessorCount, 8);
        const long perThread = 25_000_000L;

        Console.WriteLine($"  threads: {threads}   increments/thread: {perThread:N0}   total: {threads * perThread:N0}");
        Console.WriteLine();

        var sharedMs = await TimeShared(threads, perThread);
        Console.WriteLine($"  single shared counter:         {sharedMs,6} ms");

        var shardedMs = await TimeSharded(threads, perThread);
        Console.WriteLine($"  one padded counter per thread: {shardedMs,6} ms");

        Console.WriteLine();
        var ratio = shardedMs == 0 ? double.NaN : (double)sharedMs / Math.Max(1, shardedMs);
        Console.WriteLine($"  shared / sharded ratio: {ratio:F1}×");
        Console.WriteLine();
        Console.WriteLine("  The shared counter pays an RFO round-trip per increment (~30-100 ns).");
        Console.WriteLine("  Each shard stays in its writer's L1 in Modified state — no traffic at all.");
    }

    private static async Task<long> TimeShared(int threads, long perThread)
    {
        long counter = 0;
        var tasks = new Task[threads];
        var sw = Stopwatch.StartNew();
        for (var t = 0; t < threads; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (long i = 0; i < perThread; i++)
                    Interlocked.Increment(ref counter);
            });
        }
        await Task.WhenAll(tasks);
        sw.Stop();
        if (counter != threads * perThread)
            Console.WriteLine($"  (warning: counter = {counter}, expected {threads * perThread})");
        return sw.ElapsedMilliseconds;
    }

    private static async Task<long> TimeSharded(int threads, long perThread)
    {
        var shards = new PaddedLong[threads];
        var tasks = new Task[threads];
        var sw = Stopwatch.StartNew();
        for (var t = 0; t < threads; t++)
        {
            var idx = t;
            tasks[t] = Task.Run(() =>
            {
                for (long i = 0; i < perThread; i++)
                    Interlocked.Increment(ref shards[idx].Value);
            });
        }
        await Task.WhenAll(tasks);
        sw.Stop();

        long total = 0;
        for (var i = 0; i < shards.Length; i++) total += Interlocked.Read(ref shards[i].Value);
        if (total != threads * perThread)
            Console.WriteLine($"  (warning: total = {total}, expected {threads * perThread})");
        return sw.ElapsedMilliseconds;
    }

    // 128 bytes covers a 64-byte x86 line and a 128-byte Apple Silicon line, with room to spare.
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct PaddedLong
    {
        [FieldOffset(0)] public long Value;
    }
}
