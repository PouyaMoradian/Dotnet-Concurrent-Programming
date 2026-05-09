using System.Diagnostics;

namespace Chapter00.Demos;

/// <summary>
/// Measures the cost of a context switch by ping-ponging a token between two
/// threads via a pair of <see cref="ManualResetEventSlim"/>. On a Linux/Windows
/// box you should see a few microseconds per round trip; on a hot-cache, same-core
/// configuration it can be ~1 µs, while migrating across NUMA nodes it can be 10×.
/// </summary>
internal static class ContextSwitchDemo
{
    public static async Task Run()
    {
        const int rounds = 200_000;
        using var aReady = new ManualResetEventSlim(false);
        using var bReady = new ManualResetEventSlim(false);

        var a = Task.Run(() =>
        {
            for (var i = 0; i < rounds; i++)
            {
                bReady.Wait();
                bReady.Reset();
                aReady.Set();
            }
        });
        var b = Task.Run(() =>
        {
            for (var i = 0; i < rounds; i++)
            {
                aReady.Wait();
                aReady.Reset();
                bReady.Set();
            }
        });

        var sw = Stopwatch.StartNew();
        bReady.Set(); // kick off
        await Task.WhenAll(a, b);
        sw.Stop();

        var nsPerRoundTrip = sw.Elapsed.TotalMilliseconds * 1_000_000.0 / rounds;
        Console.WriteLine($"  rounds:               {rounds:N0}");
        Console.WriteLine($"  total elapsed:        {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"  ns per round-trip:    {nsPerRoundTrip:F0}");
        Console.WriteLine();
        Console.WriteLine("  A round-trip = 2 wakeups + 2 sleeps. Divide by 4 for one switch.");
    }
}
