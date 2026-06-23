using System.Diagnostics;

namespace Chapter00.Demos;

/// <summary>
/// The canonical "sorted vs unsorted" branch-prediction demonstration. The same loop
/// — same instructions, same memory access pattern — runs several times faster on a
/// sorted array than on an unsorted one. The reason is the predicate <c>a[i] &gt;= 128</c>:
/// on sorted data the predictor settles on one side after a few iterations; on random
/// data it mispredicts on ~50% of iterations, flushing the pipeline each time.
/// </summary>
internal static class BranchPredictionDemo
{
    public static Task Run()
    {
        const int n = 4 * 1024 * 1024;     // 4M elements; >> L3 to keep the loop memory-light per element
        const int repeats = 8;

        var rng = new Random(42);
        var data = new int[n];
        for (var i = 0; i < n; i++) data[i] = rng.Next() & 0xFF; // 0..255

        // Warm up the JIT.
        _ = Sum(data);

        // Unsorted run.
        var unsortedMs = TimeSum(data, repeats);

        // Sort and re-run.
        Array.Sort(data);
        var sortedMs = TimeSum(data, repeats);

        Console.WriteLine($"  elements: {n:N0}   repeats: {repeats}");
        Console.WriteLine();
        Console.WriteLine($"  unsorted: {unsortedMs,6} ms   (predicate ~50%/50% random)");
        Console.WriteLine($"  sorted:   {sortedMs,6} ms   (predicate is a single transition)");
        Console.WriteLine();
        var ratio = sortedMs == 0 ? double.NaN : (double)unsortedMs / Math.Max(1, sortedMs);
        Console.WriteLine($"  unsorted / sorted ratio: {ratio:F2}×");
        Console.WriteLine();
        Console.WriteLine("  The work is identical; the only difference is the predicate's predictability.");
        Console.WriteLine("  Mispredicts cost ~15-20 cycles each; on random data that's most iterations.");
        return Task.CompletedTask;
    }

    private static long TimeSum(int[] data, int repeats)
    {
        var sw = Stopwatch.StartNew();
        long acc = 0;
        for (var r = 0; r < repeats; r++) acc += Sum(data);
        sw.Stop();
        // Use acc so the optimiser can't drop the work.
        if (acc == long.MinValue) Console.WriteLine();
        return sw.ElapsedMilliseconds;
    }

    private static long Sum(int[] data)
    {
        long sum = 0;
        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] >= 128) sum += data[i];
        }
        return sum;
    }
}
