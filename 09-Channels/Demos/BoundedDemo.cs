using System.Diagnostics;
using System.Threading.Channels;

namespace Chapter09.Demos;

internal static class BoundedDemo
{
    public static async Task Run()
    {
        var ch = Channel.CreateBounded<int>(new BoundedChannelOptions(8)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        var sw = Stopwatch.StartNew();

        // Producer: fast (would dump 1000 items immediately).
        var producer = Task.Run(async () =>
        {
            for (var i = 0; i < 50; i++)
            {
                await ch.Writer.WriteAsync(i);     // blocks when buffer is full
            }
            ch.Writer.Complete();
        });

        // Consumer: slow (10 ms per item).
        var consumer = Task.Run(async () =>
        {
            await foreach (var x in ch.Reader.ReadAllAsync())
            {
                await Task.Delay(10);
            }
        });

        await Task.WhenAll(producer, consumer);
        sw.Stop();
        Console.WriteLine($"  total: {sw.ElapsedMilliseconds} ms (backpressure paced producer to consumer)");
    }
}
