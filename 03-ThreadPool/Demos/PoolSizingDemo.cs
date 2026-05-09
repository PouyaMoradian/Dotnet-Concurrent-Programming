namespace Chapter03.Demos;

internal static class PoolSizingDemo
{
    public static Task Run()
    {
        ThreadPool.GetMinThreads(out var minW, out var minIo);
        ThreadPool.GetMaxThreads(out var maxW, out var maxIo);
        ThreadPool.GetAvailableThreads(out var availW, out var availIo);
        Console.WriteLine($"  Min threads:  workers={minW,4}  io={minIo,4}");
        Console.WriteLine($"  Max threads:  workers={maxW,6}  io={maxIo,6}");
        Console.WriteLine($"  Available:    workers={availW,4}  io={availIo,4}");
        Console.WriteLine($"  Pending work items: {ThreadPool.PendingWorkItemCount}");
        Console.WriteLine($"  Completed work items: {ThreadPool.CompletedWorkItemCount}");
        Console.WriteLine($"  Threads alive in process: {System.Diagnostics.Process.GetCurrentProcess().Threads.Count}");
        Console.WriteLine();
        Console.WriteLine("  Tip: ThreadPool.SetMinThreads(N, N) eliminates ramp-up if you know your");
        Console.WriteLine("  steady-state concurrency. Don't raise Max unless you've measured the need.");
        return Task.CompletedTask;
    }
}
