using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Chapter01.Demos;

/// <summary>
/// Demonstrates false sharing: two unrelated counters that happen to live on the same 64-byte
/// cache line will ping-pong that line between cores. The "padded" version places each counter
/// on its own line and runs several times faster on multi-core hardware.
/// </summary>
internal static class FalseSharingDemo
{
    // Two longs packed adjacent — virtually guaranteed to share a cache line.
    private struct Packed
    {
        public long A;
        public long B;
    }

    // Pad each counter onto its own 64-byte line (the most common L1 line size).
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct Padded
    {
        [FieldOffset(0)]   public long A;
        [FieldOffset(64)]  public long B;
    }

    public static async Task Run()
    {
        const int iterations = 100_000_000;

        // Packed — two threads, each incrementing a neighbouring counter on the same line.
        var packed = new Packed();
        var sw = Stopwatch.StartNew();
        await Task.WhenAll(
            Task.Run(() => { for (int i = 0; i < iterations; i++) packed.A++; }),
            Task.Run(() => { for (int i = 0; i < iterations; i++) packed.B++; }));
        sw.Stop();
        var packedMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"  packed   counters (same cache line):  {packedMs,5} ms  ({iterations:N0} ops each)");

        // Padded — each counter on its own line.
        var padded = new Padded();
        sw.Restart();
        await Task.WhenAll(
            Task.Run(() => { for (int i = 0; i < iterations; i++) padded.A++; }),
            Task.Run(() => { for (int i = 0; i < iterations; i++) padded.B++; }));
        sw.Stop();
        var paddedMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"  padded   counters (own cache lines):  {paddedMs,5} ms");

        var ratio = packedMs == 0 ? double.NaN : (double)packedMs / Math.Max(1, paddedMs);
        Console.WriteLine();
        Console.WriteLine($"  packed/padded ratio: {ratio:F1}× (≥ 1 means false sharing is hurting)");
        Console.WriteLine("  Note: these increments are not atomic — that's fine for the demonstration");
        Console.WriteLine("  because the goal is to show cache-line traffic, not correctness. In real code");
        Console.WriteLine("  use [StructLayout(Size = 128)] or pad your hot fields away from each other.");
    }
}
