using System.Diagnostics;

namespace Chapter00.Demos;

/// <summary>
/// Demonstrates the cost of allocating-then-touching memory across many threads.
/// On a NUMA system, memory tends to be allocated near the *first thread that touches it*
/// (Linux: first-touch policy). If a different socket later reads it, the read is
/// remote and slower. We approximate the effect by running with thread affinity off
/// and observing variance.
/// </summary>
internal static class LocalityDemo
{
    public static Task Run()
    {
        const int chunks = 16;
        const int chunkSize = 4 * 1024 * 1024; // 4 MB per chunk

        var arrays = new byte[chunks][];

        // Phase 1: allocate-and-zero across many threads.
        var allocSw = Stopwatch.StartNew();
        Parallel.For(0, chunks, i =>
        {
            arrays[i] = new byte[chunkSize];
            // Touch every page so the OS commits the memory (Linux first-touch).
            for (var p = 0; p < chunkSize; p += 4096) arrays[i][p] = 1;
        });
        allocSw.Stop();

        // Phase 2: rotate which thread reads which chunk so we likely cross sockets.
        var readSw = Stopwatch.StartNew();
        long total = 0;
        Parallel.For(0, chunks, i =>
        {
            var foreign = arrays[(i + chunks / 2) % chunks];
            long s = 0;
            for (var p = 0; p < foreign.Length; p += 64) s += foreign[p];
            Interlocked.Add(ref total, s);
        });
        readSw.Stop();

        Console.WriteLine($"  allocate + zero {chunks * chunkSize / 1024 / 1024} MB:   {allocSw.ElapsedMilliseconds} ms");
        Console.WriteLine($"  cross-thread read same data:       {readSw.ElapsedMilliseconds} ms");
        Console.WriteLine($"  observed bytes (anti-elision): {total}");
        Console.WriteLine();
        Console.WriteLine("  On a NUMA box, the second phase is sensitive to first-touch placement.");
        Console.WriteLine("  Pin the process to one node (numactl --cpunodebind=0 --membind=0) and rerun");
        Console.WriteLine("  to see how much variance came from cross-socket traffic.");
        return Task.CompletedTask;
    }
}
