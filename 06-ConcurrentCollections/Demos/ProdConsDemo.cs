using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

namespace Chapter06.Demos;

internal static class ProdConsDemo
{
    public static async Task Run()
    {
        const int items = 5_000_000;

        // 1) ConcurrentQueue<T> + manual signalling.
        var q = new ConcurrentQueue<int>();
        long consumed = 0;
        var done = new ManualResetEventSlim(false);

        var sw = Stopwatch.StartNew();
        var producer = Task.Run(() =>
        {
            for (var i = 0; i < items; i++) q.Enqueue(i);
            done.Set();
        });
        var consumer = Task.Run(() =>
        {
            while (true)
            {
                while (q.TryDequeue(out _)) Interlocked.Increment(ref consumed);
                if (done.IsSet && q.IsEmpty) break;
                Thread.SpinWait(64);
            }
        });
        await Task.WhenAll(producer, consumer);
        sw.Stop();
        Console.WriteLine($"  ConcurrentQueue<T> + spin:    {sw.ElapsedMilliseconds} ms  consumed={consumed:N0}");

        // 2) Channel<int>.
        var chan = Channel.CreateBounded<int>(new BoundedChannelOptions(1024) { SingleReader = true, SingleWriter = true });
        long consumed2 = 0;

        sw.Restart();
        var p2 = Task.Run(async () =>
        {
            for (var i = 0; i < items; i++) await chan.Writer.WriteAsync(i);
            chan.Writer.Complete();
        });
        var c2 = Task.Run(async () =>
        {
            await foreach (var _ in chan.Reader.ReadAllAsync())
                consumed2++;
        });
        await Task.WhenAll(p2, c2);
        sw.Stop();
        Console.WriteLine($"  Channel<int> bounded(1024):   {sw.ElapsedMilliseconds} ms  consumed={consumed2:N0}");

        Console.WriteLine();
        Console.WriteLine("  Channel is async-friendly, gives you backpressure for free, and is typically faster.");
    }
}
