using Concurrency.Shared;

namespace Chapter03.Demos;

internal static class LongRunningDemo
{
    public static async Task Run()
    {
        // LongRunning: a hint to the pool that the task should not occupy a pool worker.
        // In current implementations this allocates a *dedicated* thread.
        var lrTask = Task.Factory.StartNew(
            () =>
            {
                Console.WriteLine($"  LongRunning task on thread: {ThreadInfo.Describe()}");
                Workloads.Cpu(100_000_000);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        var poolTask = Task.Run(() =>
        {
            Console.WriteLine($"  Task.Run task on thread:    {ThreadInfo.Describe()}");
            Workloads.Cpu(100_000_000);
        });

        await Task.WhenAll(lrTask, poolTask);
        Console.WriteLine();
        Console.WriteLine("  Use LongRunning when a task will run for *seconds-to-minutes* of CPU work.");
        Console.WriteLine("  For multi-minute workers (kafka pumps, etc.), prefer 'new Thread' + your own loop.");
    }
}
