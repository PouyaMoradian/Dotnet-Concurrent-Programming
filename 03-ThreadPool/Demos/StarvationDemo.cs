using System.Diagnostics;

namespace Chapter03.Demos;

/// <summary>
/// The classic starvation scenario: a producer schedules CPU-bound work that
/// blocks waiting for results from *other* pool tasks. The pool fills up with
/// blocked workers; the work they're waiting for can't run; throughput crawls
/// while the hill-climber slowly adds threads.
/// </summary>
internal static class StarvationDemo
{
    public static async Task Run()
    {
        // Reset min so the demo always starts the same.
        ThreadPool.SetMinThreads(Environment.ProcessorCount, Environment.ProcessorCount);

        var sw = Stopwatch.StartNew();
        var outerCount = 50;
        // Outer task waits for inner task; inner needs a worker.
        var outers = Enumerable.Range(0, outerCount).Select(_ => Task.Run(() =>
        {
            // BAD: blocking-wait on a Task that itself needs a pool worker.
            var inner = Task.Run(() => Thread.Sleep(100));
            inner.Wait();
        })).ToArray();
        await Task.WhenAll(outers);
        sw.Stop();
        Console.WriteLine($"  Naive (blocking-wait): {sw.ElapsedMilliseconds} ms with {outerCount} outers");

        // Fix: keep continuations async; never block.
        sw.Restart();
        var fixedOuters = Enumerable.Range(0, outerCount).Select(async _ =>
        {
            await Task.Run(() => Thread.Sleep(100));
        }).ToArray();
        await Task.WhenAll(fixedOuters);
        sw.Stop();
        Console.WriteLine($"  Fixed  (async wait):   {sw.ElapsedMilliseconds} ms");
        Console.WriteLine();
        Console.WriteLine("  The naive variant is ~2x slower until hill-climbing has injected enough threads.");
    }
}
