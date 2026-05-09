using System.Diagnostics;

namespace Chapter04.Demos;

/// <summary>
/// 8 threads, 5 M increments each, all on the same counter, three implementations:
/// lock(object), Interlocked.Increment, naked ++ (broken). The point: all three "look correct"
/// in source, but only the first two are.
/// </summary>
internal static class LockVsInterlocked
{
    private const int Threads = 8;
    private const int Iterations = 5_000_000;

    public static async Task Run()
    {
        var lockTime = await TimeAll(LockedRun, "lock(object)");
        var interlockedTime = await TimeAll(InterlockedRun, "Interlocked");
        var racyTime = await TimeAll(RacyRun, "racy ++");
        Console.WriteLine();
        Console.WriteLine($"  Interlocked vs lock speedup: {(double)lockTime / interlockedTime:F2}x");
        Console.WriteLine();
        Console.WriteLine("  The 'racy' counter typically lands 5-50% short of expected — race lost writes.");
    }

    private static async Task<long> TimeAll(Func<int, Task<long>> impl, string name)
    {
        var sw = Stopwatch.StartNew();
        var result = await impl(Threads);
        sw.Stop();
        Console.WriteLine($"  {name,-14}  expected {Threads * Iterations,12:N0}  got {result,12:N0}  in {sw.ElapsedMilliseconds,5} ms");
        return sw.ElapsedMilliseconds;
    }

    private static async Task<long> LockedRun(int threads)
    {
        long counter = 0;
        var sync = new object();
        await Task.WhenAll(Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < Iterations; i++)
                lock (sync) counter++;
        })));
        return counter;
    }

    private static async Task<long> InterlockedRun(int threads)
    {
        long counter = 0;
        await Task.WhenAll(Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < Iterations; i++)
                Interlocked.Increment(ref counter);
        })));
        return counter;
    }

    private static async Task<long> RacyRun(int threads)
    {
        long counter = 0;
        await Task.WhenAll(Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < Iterations; i++) counter++;
        })));
        return counter;
    }
}
