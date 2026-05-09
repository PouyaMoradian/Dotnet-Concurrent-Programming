using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;

namespace Chapter06.Demos;

internal static class ImmutableVsConcurrentDemo
{
    public static async Task Run()
    {
        const int writes = 1_000;
        const int readers = 8;
        const int readsPerThread = 1_000_000;

        var concurrent = new ConcurrentDictionary<int, int>();
        for (var i = 0; i < 1024; i++) concurrent[i] = i;

        var sw = Stopwatch.StartNew();
        await Task.WhenAll(
            Enumerable.Range(0, readers).Select(_ => Task.Run(() =>
            {
                long s = 0;
                for (var i = 0; i < readsPerThread; i++) s += concurrent[i & 1023];
                return s;
            })).Concat(new[]
            {
                Task.Run(() =>
                {
                    for (var i = 0; i < writes; i++) concurrent[i & 1023] = i;
                })
            }));
        sw.Stop();
        Console.WriteLine($"  ConcurrentDictionary read-heavy: {sw.ElapsedMilliseconds} ms");

        var immutable = ImmutableDictionary<int, int>.Empty.AddRange(
            Enumerable.Range(0, 1024).Select(i => new KeyValuePair<int, int>(i, i)));

        sw.Restart();
        await Task.WhenAll(
            Enumerable.Range(0, readers).Select(_ => Task.Run(() =>
            {
                long s = 0;
                var snap = Volatile.Read(ref immutable);
                for (var i = 0; i < readsPerThread; i++) s += snap[i & 1023];
                return s;
            })).Concat(new[]
            {
                Task.Run(() =>
                {
                    for (var i = 0; i < writes; i++)
                    {
                        ImmutableDictionary<int, int> old, next;
                        do { old = Volatile.Read(ref immutable); next = old.SetItem(i & 1023, i); }
                        while (Interlocked.CompareExchange(ref immutable, next, old) != old);
                    }
                })
            }));
        sw.Stop();
        Console.WriteLine($"  ImmutableDictionary CoW:         {sw.ElapsedMilliseconds} ms");
        Console.WriteLine();
        Console.WriteLine("  Concurrent wins for mixed read/write. Immutable wins when reads are *very* hot");
        Console.WriteLine("  (since each read hits a snapshot with zero locking).");
    }
}
