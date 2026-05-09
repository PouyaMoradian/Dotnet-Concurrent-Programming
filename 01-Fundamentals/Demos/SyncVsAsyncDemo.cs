using System.Diagnostics;

namespace Chapter01.Demos;

/// <summary>
/// 200 simulated requests, each doing 200 ms of IO.
/// Sync version uses Thread.Sleep — burns 200 worker threads.
/// Async version uses Task.Delay — burns one task object per request, no thread parking.
/// </summary>
internal static class SyncVsAsyncDemo
{
    public static async Task Run()
    {
        const int requests = 200;
        const int ioMs = 200;

        // Async — IOCP-style.
        var sw = Stopwatch.StartNew();
        var asyncTasks = Enumerable.Range(0, requests).Select(_ => Task.Delay(ioMs));
        await Task.WhenAll(asyncTasks);
        sw.Stop();
        Console.WriteLine($"  async ({requests} × {ioMs}ms IO): {sw.ElapsedMilliseconds} ms, peak threads ≈ {Process.GetCurrentProcess().Threads.Count}");

        // Sync — actually parks threads. The pool grows under starvation.
        sw.Restart();
        var syncTasks = Enumerable.Range(0, requests).Select(_ => Task.Run(() => Thread.Sleep(ioMs)));
        await Task.WhenAll(syncTasks);
        sw.Stop();
        Console.WriteLine($"  sync  ({requests} × {ioMs}ms sleep): {sw.ElapsedMilliseconds} ms, peak threads ≈ {Process.GetCurrentProcess().Threads.Count}");

        Console.WriteLine();
        Console.WriteLine("  Sync 'works' but starves the pool — see hill-climbing in Chapter 03.");
    }
}
