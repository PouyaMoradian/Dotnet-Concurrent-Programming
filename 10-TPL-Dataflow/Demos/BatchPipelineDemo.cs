using System.Threading.Tasks.Dataflow;

namespace Chapter10.Demos;

internal static class BatchPipelineDemo
{
    public static async Task Run()
    {
        var batcher = new BatchBlock<int>(50);
        var sink = new ActionBlock<int[]>(batch =>
        {
            Console.WriteLine($"   sink received batch of {batch.Length}, sum={batch.Sum()}");
        });
        batcher.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });

        for (var i = 0; i < 200; i++) await batcher.SendAsync(i);
        batcher.Complete();
        await sink.Completion;
    }
}
