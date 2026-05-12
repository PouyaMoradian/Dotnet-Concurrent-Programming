using System.Diagnostics;

namespace Chapter00.Demos;

/// <summary>
/// Walks linked-list-style pointer chains through arrays of doubling sizes. The "next"
/// index is computed in a way that defeats the hardware prefetcher (each step's address
/// depends on the value read at the previous step). The reported ns/access steps up
/// as the working set crosses each cache level. The location of the steps reveals
/// your machine's L1/L2/L3/DRAM sizes.
/// </summary>
internal static class MemoryLatencyLadderDemo
{
    public static Task Run()
    {
        var sizesKb = new[] { 8, 16, 32, 64, 96, 128, 192, 256, 384, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536, 131072, 262144 };

        Console.WriteLine($"  size (KB)   ns / access   level (rough)");
        Console.WriteLine($"  ─────────   ───────────   ─────────────");

        foreach (var kb in sizesKb)
        {
            var bytes = kb * 1024;
            var elements = bytes / sizeof(int);
            var ns = MeasureRandomChase(elements);
            var hint = ClassifyLevel(kb, ns);
            Console.WriteLine($"  {kb,8}    {ns,10:F1}    {hint}");
        }

        Console.WriteLine();
        Console.WriteLine("  Look for the *steps* where ns/access jumps. Those are the cache-level transitions.");
        Console.WriteLine("  Typical: ~1 ns in L1, ~3-5 ns in L2, ~10-15 ns in L3, ~80-100 ns in DRAM.");
        Console.WriteLine("  Apple Silicon and recent server chips can be 20-30% faster at each level.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Build a pseudo-random permutation of the indices and walk it. Each step's address
    /// depends on the previous step's read, defeating the prefetcher. We measure the average
    /// time per step.
    /// </summary>
    private static double MeasureRandomChase(int elements)
    {
        // Build a permutation: arr[i] = next index to visit. The cycle visits every element once.
        var arr = new int[elements];
        for (var i = 0; i < elements; i++) arr[i] = i;
        Shuffle(arr, seed: 1);

        // Turn it into a single permutation cycle by remapping (so we always advance).
        var perm = new int[elements];
        for (var i = 0; i < elements - 1; i++) perm[arr[i]] = arr[i + 1];
        perm[arr[elements - 1]] = arr[0];

        // Warm up.
        Walk(perm, Math.Min(200_000, elements));

        // Cap total steps so the largest sizes don't run for minutes. We still get a
        // statistically clean reading because we measure ns per step, not total time.
        long steps = Math.Clamp(20L * elements, 2_000_000L, 50_000_000L);
        var sw = Stopwatch.StartNew();
        var sink = Walk(perm, steps);
        sw.Stop();
        if (sink == int.MinValue) Console.WriteLine(); // anti-elision
        return sw.Elapsed.TotalMilliseconds * 1_000_000.0 / steps;
    }

    private static int Walk(int[] perm, long steps)
    {
        var cur = 0;
        for (long i = 0; i < steps; i++) cur = perm[cur];
        return cur;
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

    private static string ClassifyLevel(int sizeKb, double ns)
    {
        if (sizeKb <= 48 && ns < 3) return "L1d";
        if (sizeKb <= 1024 && ns < 8) return "L2";
        if (sizeKb <= 64 * 1024 && ns < 40) return "L3";
        return "DRAM";
    }
}
