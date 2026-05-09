using System.Diagnostics;

namespace Chapter01.Demos;

/// <summary>
/// Crude comparison of "spin up a thread" vs "spin up a Task" vs "spin up a process".
/// Numbers are wildly different by orders of magnitude; that's the point.
/// </summary>
internal static class ProcessVsThreadDemo
{
    public static async Task Run()
    {
        const int count = 1_000;

        // Threads: explicit, costly. Each new Thread allocates a 1 MB user-mode stack by default.
        var sw = Stopwatch.StartNew();
        var threads = new Thread[count];
        for (var i = 0; i < count; i++)
        {
            threads[i] = new Thread(static () => { /* no-op */ }) { IsBackground = true };
            threads[i].Start();
        }
        foreach (var t in threads) t.Join();
        sw.Stop();
        Console.WriteLine($"  {count} dedicated Threads:  start+join = {sw.ElapsedMilliseconds,5} ms");

        // Tasks on the thread pool: pooled, cheap.
        sw.Restart();
        var tasks = new Task[count];
        for (var i = 0; i < count; i++) tasks[i] = Task.Run(static () => { /* no-op */ });
        await Task.WhenAll(tasks);
        sw.Stop();
        Console.WriteLine($"  {count} Tasks (Task.Run):   start+wait = {sw.ElapsedMilliseconds,5} ms");

        // Async no-op: completes synchronously, never even queues.
        sw.Restart();
        var asyncTasks = new Task[count];
        for (var i = 0; i < count; i++) asyncTasks[i] = NoOpAsync();
        await Task.WhenAll(asyncTasks);
        sw.Stop();
        Console.WriteLine($"  {count} async no-ops:       start+wait = {sw.ElapsedMilliseconds,5} ms");

        Console.WriteLine();
        Console.WriteLine("  Threads: ~1 MB stack each, kernel scheduling unit.");
        Console.WriteLine("  Tasks:   pooled stacks; the pool decides when to grow.");
        Console.WriteLine("  Async:   completes inline — no thread, no task object if synchronous.");
    }

    private static Task NoOpAsync() => Task.CompletedTask;
}
