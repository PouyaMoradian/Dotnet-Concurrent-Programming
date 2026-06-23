using System.Diagnostics;

namespace Chapter01.Demos;

/// <summary>
/// The textbook deadlock: two locks, two threads, opposite acquisition orders. The naïve
/// version deadlocks forever; we demonstrate that with a hard timeout and a separate run that
/// uses <see cref="Monitor.TryEnter(object, TimeSpan)"/> to back off and retry.
/// </summary>
internal static class DeadlockDemo
{
    private static readonly object _lockA = new();
    private static readonly object _lockB = new();

    public static async Task Run()
    {
        await DemonstrateDeadlock();
        await DemonstrateTryEnter();

        Console.WriteLine();
        Console.WriteLine("  Lock ordering is the cheapest defence: pick a global order (by lock identity");
        Console.WriteLine("  hashCode, by name, by id) and always acquire in that order. TryEnter + back-off");
        Console.WriteLine("  is the next line of defence when ordering isn't possible.");
    }

    private static async Task DemonstrateDeadlock()
    {
        Console.WriteLine("  -- naïve order (deadlocks; we time out at 2 s) --");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var sw = Stopwatch.StartNew();

        // Thread 1: A then B.
        // Thread 2: B then A.
        // With high probability one thread holds A while the other holds B, and neither can proceed.
        var t1 = Task.Run(() =>
        {
            lock (_lockA)
            {
                Thread.Sleep(10);
                if (cts.IsCancellationRequested) return;
                lock (_lockB) { /* never reached */ }
            }
        }, cts.Token);

        var t2 = Task.Run(() =>
        {
            lock (_lockB)
            {
                Thread.Sleep(10);
                if (cts.IsCancellationRequested) return;
                lock (_lockA) { /* never reached */ }
            }
        }, cts.Token);

        try { await Task.WhenAll(t1, t2).WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TimeoutException) { /* expected */ }

        sw.Stop();
        Console.WriteLine($"  observed deadlock; gave up after {sw.ElapsedMilliseconds} ms");

        // Both threads are still stuck in their locks; we can't recover. Real apps would tear down
        // and report. We let the process keep going by leaking these tasks — they'll never complete.
    }

    private static async Task DemonstrateTryEnter()
    {
        Console.WriteLine();
        Console.WriteLine("  -- TryEnter + back-off (always finishes) --");

        var lockA = new object();
        var lockB = new object();
        const int iterations = 500;
        var done = 0;
        var retries = 0;

        async Task Worker(object first, object second)
        {
            for (var i = 0; i < iterations; i++)
            {
                while (true)
                {
                    if (Monitor.TryEnter(first, TimeSpan.FromMilliseconds(5)))
                    {
                        try
                        {
                            if (Monitor.TryEnter(second, TimeSpan.FromMilliseconds(5)))
                            {
                                try { /* critical section */ } finally { Monitor.Exit(second); }
                                break;
                            }
                        }
                        finally { Monitor.Exit(first); }
                    }
                    Interlocked.Increment(ref retries);
                    await Task.Yield();
                }
                Interlocked.Increment(ref done);
            }
        }

        var sw = Stopwatch.StartNew();
        await Task.WhenAll(
            Task.Run(() => Worker(lockA, lockB)),
            Task.Run(() => Worker(lockB, lockA)));
        sw.Stop();

        Console.WriteLine($"  finished {done:N0} acquisitions in {sw.ElapsedMilliseconds} ms with {retries:N0} retries");
    }
}
