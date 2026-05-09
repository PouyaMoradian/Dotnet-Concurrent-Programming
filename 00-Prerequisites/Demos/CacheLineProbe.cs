using System.Diagnostics;

namespace Chapter00.Demos;

/// <summary>
/// Probes the effective cache-line size by walking an array with a stride
/// and timing it. The fastest stride that still touches every line is roughly
/// one access per cache line, so the time-per-element drops sharply when
/// the stride exceeds the line size (typically 64 bytes on x86, 128 on ARM).
/// </summary>
internal static class CacheLineProbe
{
    public static Task Run()
    {
        const int sizeBytes = 64 * 1024 * 1024; // 64 MB — bigger than most L2/L3.
        var data = new byte[sizeBytes];

        Console.WriteLine($"  array size: {sizeBytes / 1024 / 1024} MB");
        Console.WriteLine();
        Console.WriteLine($"  stride   total touches    time(ms)   ns/touch");

        // Larger strides perform fewer accesses but each access likely incurs an L3/DRAM miss.
        // We normalise to ns per touch so they're comparable.
        foreach (var stride in new[] { 1, 8, 16, 32, 64, 128, 256, 512, 1024 })
        {
            // Warm-up.
            Walk(data, stride);

            var sw = Stopwatch.StartNew();
            var touches = Walk(data, stride);
            sw.Stop();

            var nsPerTouch = sw.Elapsed.TotalMilliseconds * 1_000_000.0 / touches;
            Console.WriteLine($"  {stride,6}   {touches,12:N0}   {sw.ElapsedMilliseconds,8}   {nsPerTouch,8:F2}");
        }

        Console.WriteLine();
        Console.WriteLine("  Look for the stride at which ns/touch jumps — that's roughly your line size.");
        return Task.CompletedTask;
    }

    private static long Walk(byte[] data, int stride)
    {
        long sum = 0;
        for (var i = 0; i < data.Length; i += stride)
        {
            sum += data[i];
        }
        return data.Length / stride;
    }
}
