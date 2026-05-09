using System.Diagnostics;

namespace Chapter07.Demos;

internal static class ParallelForDemo
{
    public static Task Run()
    {
        const int n = 100_000_000;
        var data = new int[n];
        for (var i = 0; i < n; i++) data[i] = i & 1023;

        // 1) sequential.
        var sw = Stopwatch.StartNew();
        long s1 = 0;
        for (var i = 0; i < n; i++) s1 += data[i];
        sw.Stop();
        Console.WriteLine($"  sequential:                {sw.ElapsedMilliseconds,5} ms  sum={s1}");

        // 2) Parallel.For with shared accumulator (BAD — every iteration contends).
        long shared = 0;
        sw.Restart();
        Parallel.For(0, n, i => Interlocked.Add(ref shared, data[i]));
        sw.Stop();
        Console.WriteLine($"  Parallel.For + Interlocked: {sw.ElapsedMilliseconds,5} ms  sum={shared}");

        // 3) Parallel.For with thread-local accumulator (GOOD).
        long localTotal = 0;
        sw.Restart();
        Parallel.For(0, n,
            localInit: () => 0L,
            body: (i, _, local) => local + data[i],
            localFinally: local => Interlocked.Add(ref localTotal, local));
        sw.Stop();
        Console.WriteLine($"  Parallel.For + local agg:   {sw.ElapsedMilliseconds,5} ms  sum={localTotal}");
        Console.WriteLine();
        Console.WriteLine("  The local-agg version contends only once per partition (a few times total),");
        Console.WriteLine("  not once per element. Almost always the right shape for parallel reductions.");
        return Task.CompletedTask;
    }
}
