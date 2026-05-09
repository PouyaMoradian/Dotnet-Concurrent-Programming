using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Chapter00.Demos;

/// <summary>
/// Two threads each increment their own counter. Logically they share nothing.
/// But if the two counters live on the same 64-byte cache line, the line
/// ping-pongs between L1 caches via MESI invalidations and the program runs
/// up to ~10× slower than when the counters are padded onto separate lines.
/// </summary>
internal static class FalseSharingDemo
{
    private const int Iterations = 200_000_000;

    public static async Task Run()
    {
        Console.WriteLine($"  iterations per thread: {Iterations:N0}");
        Console.WriteLine();

        var packed = new Packed();
        var packedTime = await TimePair(
            () => Spin(ref packed.A),
            () => Spin(ref packed.B));
        Console.WriteLine($"  packed (same line):     {packedTime} ms");

        var padded = new Padded();
        var paddedTime = await TimePair(
            () => SpinPadded(ref padded.A),
            () => SpinPadded(ref padded.B));
        Console.WriteLine($"  padded (separate lines): {paddedTime} ms");

        Console.WriteLine();
        Console.WriteLine($"  speedup from padding: {(double)packedTime / Math.Max(1, paddedTime):F2}x");
    }

    private static async Task<long> TimePair(Action a, Action b)
    {
        var sw = Stopwatch.StartNew();
        var ta = Task.Run(a);
        var tb = Task.Run(b);
        await Task.WhenAll(ta, tb);
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    private static void Spin(ref long counter)
    {
        for (var i = 0; i < Iterations; i++) counter++;
    }

    private static void SpinPadded(ref PaddedLong counter)
    {
        for (var i = 0; i < Iterations; i++) counter.Value++;
    }

    // Packed: both fields share the first 16 bytes — guaranteed same cache line.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Packed
    {
        public long A;
        public long B;
    }

    // Padded: the runtime guarantees separate cache lines.
    [StructLayout(LayoutKind.Sequential)]
    private struct Padded
    {
        public PaddedLong A;
        public PaddedLong B;
    }

    [StructLayout(LayoutKind.Explicit, Size = 128)] // two cache lines on most x86, one on Apple Silicon (128-byte L1 line).
    private struct PaddedLong
    {
        [FieldOffset(0)] public long Value;
    }
}
