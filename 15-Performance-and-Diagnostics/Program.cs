using Concurrency.Diagnostics;
using Concurrency.Shared;

await ConsoleLab.Run("Chapter 15 — Performance and Diagnostics",
[
    ("Emit EventSource events", () =>
    {
        ConcurrencyEventSource.Log.DemoStarted("ChapterDemo");
        for (var i = 0; i < 5; i++) ConcurrencyEventSource.Log.Step($"step-{i}");
        ConcurrencyEventSource.Log.DemoFinished("ChapterDemo", 42);
        Console.WriteLine("  Subscribe with:");
        Console.WriteLine("    dotnet-trace collect --process-id <pid> --providers DotnetConcurrency-Demo");
        return Task.CompletedTask;
    }),
    ("Show in-process counters", () =>
    {
        ThreadPool.GetAvailableThreads(out var w, out var io);
        Console.WriteLine($"  workers free = {w}, io free = {io}");
        Console.WriteLine($"  pending work = {ThreadPool.PendingWorkItemCount}");
        Console.WriteLine($"  completed    = {ThreadPool.CompletedWorkItemCount}");
        Console.WriteLine($"  alive threads in process = {System.Diagnostics.Process.GetCurrentProcess().Threads.Count}");
        return Task.CompletedTask;
    }),
],
args);
