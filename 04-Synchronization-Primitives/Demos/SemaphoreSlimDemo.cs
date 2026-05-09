using System.Diagnostics;
using Concurrency.Shared;

namespace Chapter04.Demos;

internal static class SemaphoreSlimDemo
{
    public static async Task Run()
    {
        // 100 simulated outbound HTTP calls; we want at most 4 in flight.
        const int total = 100;
        const int concurrency = 4;

        using var gate = new SemaphoreSlim(initialCount: concurrency, maxCount: concurrency);
        var inFlight = 0;
        var maxObserved = 0;

        var sw = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, total).Select(async _ =>
        {
            await gate.WaitAsync();
            try
            {
                var current = Interlocked.Increment(ref inFlight);
                int prev;
                do { prev = maxObserved; if (current <= prev) break; }
                while (Interlocked.CompareExchange(ref maxObserved, current, prev) != prev);

                await Workloads.Io(50);
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
                gate.Release();
            }
        }));
        sw.Stop();

        Console.WriteLine($"  total tasks:           {total}");
        Console.WriteLine($"  cap:                   {concurrency}");
        Console.WriteLine($"  max in-flight observed: {maxObserved}  (must equal cap)");
        Console.WriteLine($"  total time:             {sw.ElapsedMilliseconds} ms (expect ≈ {total * 50.0 / concurrency} ms)");
    }
}
