using System.Diagnostics;

namespace Chapter00.Demos;

/// <summary>
/// A single dependency chain serialises through the pipeline. Splitting the same
/// arithmetic across multiple independent accumulators lets the CPU run them in
/// parallel through different execution ports. The work is identical; only the
/// dataflow changes. Expect ~3-4× speedup from four accumulators on x86-64 and
/// Apple Silicon — without touching SIMD.
/// </summary>
internal static class InstructionLevelParallelismDemo
{
    public static Task Run()
    {
        const int n = 16 * 1024 * 1024;     // ~64 MB of ints; mostly memory-bound
        const int repeats = 4;

        var data = new int[n];
        for (var i = 0; i < n; i++) data[i] = i + 1;

        // Warmup.
        _ = SumSingle(data);
        _ = SumQuad(data);

        var singleMs = Time(() => SumSingle(data), repeats);
        var quadMs = Time(() => SumQuad(data), repeats);

        Console.WriteLine($"  elements: {n:N0}   repeats: {repeats}");
        Console.WriteLine();
        Console.WriteLine($"  one accumulator:   {singleMs,6} ms   (each add waits for the previous)");
        Console.WriteLine($"  four accumulators: {quadMs,6} ms   (four independent chains)");
        Console.WriteLine();
        var ratio = quadMs == 0 ? double.NaN : (double)singleMs / Math.Max(1, quadMs);
        Console.WriteLine($"  single / quad ratio: {ratio:F2}×");
        Console.WriteLine();
        Console.WriteLine("  The CPU is wide enough to retire 4+ adds per cycle when they don't depend");
        Console.WriteLine("  on each other. A single accumulator forces a 1-per-cycle serial chain.");
        return Task.CompletedTask;
    }

    private static long Time(Func<long> work, int repeats)
    {
        var sw = Stopwatch.StartNew();
        long acc = 0;
        for (var r = 0; r < repeats; r++) acc += work();
        sw.Stop();
        if (acc == long.MinValue) Console.WriteLine();   // anti-elision
        return sw.ElapsedMilliseconds;
    }

    // Dependent chain: every iteration depends on the previous sum.
    private static long SumSingle(int[] data)
    {
        long sum = 0;
        for (var i = 0; i < data.Length; i++) sum += data[i];
        return sum;
    }

    // Four independent chains: the CPU can dispatch all four adds in the same cycle.
    private static long SumQuad(int[] data)
    {
        long s0 = 0, s1 = 0, s2 = 0, s3 = 0;
        var i = 0;
        for (; i + 3 < data.Length; i += 4)
        {
            s0 += data[i + 0];
            s1 += data[i + 1];
            s2 += data[i + 2];
            s3 += data[i + 3];
        }
        long sum = s0 + s1 + s2 + s3;
        for (; i < data.Length; i++) sum += data[i];
        return sum;
    }
}
