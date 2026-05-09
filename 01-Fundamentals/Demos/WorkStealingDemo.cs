using System.Diagnostics;
using Concurrency.Shared;

namespace Chapter01.Demos;

/// <summary>
/// Hints at how the .NET ThreadPool's work-stealing helps when one worker
/// has a deeper queue than another. We schedule all work from one thread
/// (so it lands on that thread's local queue) then let the rest of the pool steal.
/// </summary>
internal static class WorkStealingDemo
{
    public static async Task Run()
    {
        const int items = 1_000;

        var sw = Stopwatch.StartNew();
        // Tasks created from a worker land on that worker's local LIFO queue.
        // Other workers, when idle, steal from the *bottom* (FIFO) of someone else's queue.
        var tasks = new List<Task>(items);
        await Task.Run(() =>
        {
            for (var i = 0; i < items; i++)
                tasks.Add(Task.Run(() => Workloads.Cpu(500_000)));
        });
        await Task.WhenAll(tasks);
        sw.Stop();

        Console.WriteLine($"  {items} pool tasks, ~500k iters each:  {sw.ElapsedMilliseconds} ms");
        Console.WriteLine();
        Console.WriteLine("  Work-stealing is why the pool stays busy even when one producer creates");
        Console.WriteLine("  all the tasks. We dive into the local-LIFO + global-FIFO + steal-FIFO design");
        Console.WriteLine("  in Chapter 03.");
    }
}
