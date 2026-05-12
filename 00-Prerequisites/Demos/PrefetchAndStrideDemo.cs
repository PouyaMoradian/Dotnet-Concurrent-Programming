using System.Diagnostics;

namespace Chapter00.Demos;

/// <summary>
/// Two passes over the same number of cache lines, with the same total bytes touched:
/// one sequential (stride = 1 line), one random (a shuffled permutation). The
/// sequential pass is much faster because the hardware prefetcher hides the DRAM
/// latency, while the random pass forces a cold cache miss on every step. Same
/// memory, same arithmetic — only the access order differs.
/// </summary>
internal static class PrefetchAndStrideDemo
{
    public static Task Run()
    {
        const int sizeMb = 128;
        var bytes = sizeMb * 1024 * 1024;
        var elements = bytes / sizeof(int);
        var data = new int[elements];

        // Touch every page so we measure steady state, not first-touch.
        for (var i = 0; i < elements; i += 1024) data[i] = i;

        const int linesPerInt = sizeof(int);
        var stride = 64 / linesPerInt;        // 16 ints per 64-byte line
        var lineCount = elements / stride;

        // Sequential indices.
        var seq = new int[lineCount];
        for (var i = 0; i < lineCount; i++) seq[i] = i * stride;

        // Random permutation of the same indices.
        var rnd = new int[lineCount];
        Array.Copy(seq, rnd, lineCount);
        Shuffle(rnd, seed: 7);

        // Warm up.
        _ = Walk(data, seq);
        _ = Walk(data, rnd);

        var seqMs = TimeWalk(data, seq);
        var rndMs = TimeWalk(data, rnd);

        Console.WriteLine($"  array size: {sizeMb} MB   lines touched: {lineCount:N0}");
        Console.WriteLine();
        Console.WriteLine($"  sequential walk (stride 64 B): {seqMs,6} ms");
        Console.WriteLine($"  random walk    (same lines):   {rndMs,6} ms");
        Console.WriteLine();
        var ratio = seqMs == 0 ? double.NaN : (double)rndMs / Math.Max(1, seqMs);
        Console.WriteLine($"  random / sequential ratio: {ratio:F1}×");
        Console.WriteLine();
        Console.WriteLine("  Same number of cache lines fetched; only the order differs.");
        Console.WriteLine("  The prefetcher hides latency on the sequential pass; nothing helps the random one.");
        return Task.CompletedTask;
    }

    private static long Walk(int[] data, int[] indices)
    {
        long sum = 0;
        for (var i = 0; i < indices.Length; i++) sum += data[indices[i]];
        return sum;
    }

    private static long TimeWalk(int[] data, int[] indices)
    {
        var sw = Stopwatch.StartNew();
        var sum = Walk(data, indices);
        sw.Stop();
        if (sum == long.MinValue) Console.WriteLine();   // anti-elision
        return sw.ElapsedMilliseconds;
    }

    private static void Shuffle(int[] a, int seed)
    {
        var rng = new Random(seed);
        for (var i = a.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (a[i], a[j]) = (a[j], a[i]);
        }
    }
}
