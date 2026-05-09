using System.Threading.Tasks.Dataflow;

namespace Chapter10.Demos;

internal static class LinearPipelineDemo
{
    public static async Task Run()
    {
        var transform = new TransformBlock<int, int>(
            async x => { await Task.Delay(5); return x * 2; },
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 4,
                BoundedCapacity = 16,
            });

        long total = 0;
        var sink = new ActionBlock<int>(
            x => Interlocked.Add(ref total, x),
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 1,
                BoundedCapacity = 16,
            });

        transform.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });

        for (var i = 0; i < 1000; i++) await transform.SendAsync(i);
        transform.Complete();
        await sink.Completion;

        Console.WriteLine($"  total: {total}  (expected {Enumerable.Range(0, 1000).Sum(i => i * 2)})");
    }
}
