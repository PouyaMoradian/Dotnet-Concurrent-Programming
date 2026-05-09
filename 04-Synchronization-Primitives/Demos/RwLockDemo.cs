using System.Diagnostics;

namespace Chapter04.Demos;

/// <summary>
/// Shows when ReaderWriterLockSlim helps and when it hurts.
/// Heavy-reader workloads with rare writes — wins big.
/// Mostly-write workloads — loses (RWLockSlim has more bookkeeping than a plain lock).
/// </summary>
internal static class RwLockDemo
{
    public static async Task Run()
    {
        await Compare(readers: 8, writers: 0, durationMs: 2000);
        await Compare(readers: 8, writers: 1, durationMs: 2000);
        await Compare(readers: 1, writers: 8, durationMs: 2000);
    }

    private static async Task Compare(int readers, int writers, int durationMs)
    {
        Console.WriteLine($"  --- readers={readers} writers={writers} ---");
        var (readsLock, writesLock) = await Run(usingRwLock: false, readers, writers, durationMs);
        var (readsRw, writesRw) = await Run(usingRwLock: true, readers, writers, durationMs);
        Console.WriteLine($"   lock        : reads={readsLock,12:N0}  writes={writesLock,9:N0}");
        Console.WriteLine($"   RWLockSlim  : reads={readsRw,12:N0}  writes={writesRw,9:N0}");
        Console.WriteLine();
    }

    private static async Task<(long reads, long writes)> Run(bool usingRwLock, int readers, int writers, int durationMs)
    {
        var sync = new object();
        var rw = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        var data = new long[64];          // arbitrary
        long reads = 0, writes = 0;

        using var stop = new CancellationTokenSource(durationMs);

        var rTasks = Enumerable.Range(0, readers).Select(_ => Task.Run(() =>
        {
            long sum = 0;
            while (!stop.IsCancellationRequested)
            {
                if (usingRwLock) { rw.EnterReadLock(); try { foreach (var x in data) sum += x; } finally { rw.ExitReadLock(); } }
                else             { lock (sync)            { foreach (var x in data) sum += x; } }
                Interlocked.Increment(ref reads);
            }
            return sum;
        }));

        var wTasks = Enumerable.Range(0, writers).Select(_ => Task.Run(() =>
        {
            var i = 0;
            while (!stop.IsCancellationRequested)
            {
                if (usingRwLock) { rw.EnterWriteLock(); try { data[i++ % data.Length]++; } finally { rw.ExitWriteLock(); } }
                else             { lock (sync)            { data[i++ % data.Length]++; } }
                Interlocked.Increment(ref writes);
            }
        }));

        await Task.WhenAll(rTasks.Concat(wTasks));
        return (reads, writes);
    }
}
