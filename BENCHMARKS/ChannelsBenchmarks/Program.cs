using System.Collections.Concurrent;
using System.Threading.Channels;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

BenchmarkRunner.Run<ChannelsBench>();

[MemoryDiagnoser]
public class ChannelsBench
{
    [Params(10_000, 100_000)] public int Items;

    [Benchmark(Baseline = true)]
    public async Task<long> ChannelBoundedSpsc()
    {
        var ch = Channel.CreateBounded<int>(new BoundedChannelOptions(1024)
        {
            SingleReader = true, SingleWriter = true,
        });
        var sum = 0L;
        var consumer = Task.Run(async () =>
        {
            await foreach (var x in ch.Reader.ReadAllAsync()) sum += x;
        });
        for (var i = 0; i < Items; i++) await ch.Writer.WriteAsync(i);
        ch.Writer.Complete();
        await consumer;
        return sum;
    }

    [Benchmark]
    public async Task<long> ChannelUnbounded()
    {
        var ch = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
        {
            SingleReader = true, SingleWriter = true,
        });
        var sum = 0L;
        var consumer = Task.Run(async () =>
        {
            await foreach (var x in ch.Reader.ReadAllAsync()) sum += x;
        });
        for (var i = 0; i < Items; i++) await ch.Writer.WriteAsync(i);
        ch.Writer.Complete();
        await consumer;
        return sum;
    }

    [Benchmark]
    public async Task<long> BlockingCollectionBaseline()
    {
        using var bc = new BlockingCollection<int>(1024);
        var sum = 0L;
        var consumer = Task.Run(() =>
        {
            foreach (var x in bc.GetConsumingEnumerable()) sum += x;
        });
        for (var i = 0; i < Items; i++) bc.Add(i);
        bc.CompleteAdding();
        await consumer;
        return sum;
    }
}
