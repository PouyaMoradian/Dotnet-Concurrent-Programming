using System.Diagnostics;
using Concurrency.Shared;

namespace Chapter07.Demos;

internal static class ParallelForEachAsyncDemo
{
    public static async Task Run()
    {
        const int items = 100;
        const int ioMs = 50;

        // BAD: Parallel.ForEach over async — pins workers; offers no cap that respects async.
        var sw = Stopwatch.StartNew();
        Parallel.ForEach(Enumerable.Range(0, items), _ =>
        {
            Task.Delay(ioMs).GetAwaiter().GetResult();   // sync-over-async; ick
        });
        sw.Stop();
        Console.WriteLine($"  Parallel.ForEach (sync-over-async): {sw.ElapsedMilliseconds} ms");

        // GOOD: Parallel.ForEachAsync with explicit cap; no thread pinning.
        sw.Restart();
        await Parallel.ForEachAsync(
            Enumerable.Range(0, items),
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            async (_, ct) => await Workloads.Io(ioMs, ct));
        sw.Stop();
        Console.WriteLine($"  Parallel.ForEachAsync (cap=8):       {sw.ElapsedMilliseconds} ms");

        Console.WriteLine();
        Console.WriteLine("  Use Parallel.ForEachAsync for IO-bound fan-out. Workers are not pinned.");
        Console.WriteLine("  CancellationToken passed to the body; pass yours through ParallelOptions.");
    }
}
