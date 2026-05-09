using System.Diagnostics;
using System.Threading.Tasks.Dataflow;

namespace Chapter10.Demos;

internal static class BackpressureDemo
{
    public static async Task Run()
    {
        // Slow sink (10 ms/item) with small bounded capacity (4).
        var sink = new ActionBlock<int>(
            async _ => await Task.Delay(10),
            new ExecutionDataflowBlockOptions { BoundedCapacity = 4 });

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 200; i++)
        {
            // SendAsync respects BoundedCapacity — it awaits when the block is full.
            await sink.SendAsync(i);
        }
        sink.Complete();
        await sink.Completion;
        sw.Stop();

        Console.WriteLine($"  feeding 200 items into a slow sink: {sw.ElapsedMilliseconds} ms");
        Console.WriteLine("  the producer was paced by the sink's bound — that's backpressure across a Dataflow link.");
    }
}
