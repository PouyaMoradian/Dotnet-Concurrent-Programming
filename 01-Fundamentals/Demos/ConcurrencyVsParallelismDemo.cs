using System.Diagnostics;
using Concurrency.Shared;

namespace Chapter01.Demos;

/// <summary>
/// Demonstrates that "concurrent" and "parallel" are not the same thing.
/// Three tasks doing IO concurrently on one logical thread complete in ~max(IO),
/// not sum(IO), even though no parallelism happened.
/// </summary>
internal static class ConcurrencyVsParallelismDemo
{
    public static async Task Run()
    {
        // Concurrent — three async waits run interleaved.
        var sw = Stopwatch.StartNew();
        await Task.WhenAll(Workloads.Io(300), Workloads.Io(300), Workloads.Io(300));
        sw.Stop();
        Console.WriteLine($"  3 × 300ms IO concurrently:        {sw.ElapsedMilliseconds} ms (≈ max, not sum)");

        // Parallel — three CPU loops actually executing at the same time.
        sw.Restart();
        Parallel.Invoke(
            () => Workloads.Cpu(20_000_000),
            () => Workloads.Cpu(20_000_000),
            () => Workloads.Cpu(20_000_000));
        sw.Stop();
        Console.WriteLine($"  3 × CPU loops in parallel:        {sw.ElapsedMilliseconds} ms");

        // Sequential CPU — baseline.
        sw.Restart();
        Workloads.Cpu(20_000_000);
        Workloads.Cpu(20_000_000);
        Workloads.Cpu(20_000_000);
        sw.Stop();
        Console.WriteLine($"  3 × CPU loops sequentially:       {sw.ElapsedMilliseconds} ms");

        Console.WriteLine();
        Console.WriteLine("  Concurrency hides latency. Parallelism shortens compute.");
    }
}
