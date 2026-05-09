using System.Diagnostics;

namespace Chapter03.Demos;

/// <summary>
/// Schedule far more work than there are workers and watch the pool grow.
/// The thread count will not jump immediately; it grows in ~500 ms ticks
/// as the hill-climbing controller perturbs the count and observes throughput.
/// </summary>
internal static class HillClimbDemo
{
    public static async Task Run()
    {
        const int workItems = 200;

        Console.WriteLine($"  Queueing {workItems} blocking 200ms work items.");
        Console.WriteLine();
        Console.WriteLine($"  time(s)   threads-in-process   workers-busy");

        var stopMonitor = new CancellationTokenSource();
        var monitor = Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            while (!stopMonitor.IsCancellationRequested)
            {
                ThreadPool.GetAvailableThreads(out var availW, out _);
                ThreadPool.GetMaxThreads(out var maxW, out _);
                Console.WriteLine($"  {sw.Elapsed.TotalSeconds,6:F1}        {Process.GetCurrentProcess().Threads.Count,5}              {maxW - availW,5}");
                await Task.Delay(250);
            }
        });

        var tasks = Enumerable.Range(0, workItems).Select(_ => Task.Run(() => Thread.Sleep(200))).ToArray();
        await Task.WhenAll(tasks);

        stopMonitor.Cancel();
        try { await monitor; } catch { }
        Console.WriteLine();
        Console.WriteLine("  Notice the staircase shape: threads added in chunks every ~0.5s.");
    }
}
