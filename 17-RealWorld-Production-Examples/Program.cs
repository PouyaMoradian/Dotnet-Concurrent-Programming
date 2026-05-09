using System.Threading.Channels;
using Concurrency.Shared;

await ConsoleLab.Run("Chapter 17 — Real-World Production Examples",
[
    ("Background worker service shape", BackgroundWorkerDemo),
    ("Sharded multi-worker pipeline",   ShardedPipelineDemo),
],
args);

static async Task BackgroundWorkerDemo()
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    var ingress = Channel.CreateBounded<int>(64);

    // Producer — would be HTTP / Kafka in production.
    var producer = Task.Run(async () =>
    {
        for (var i = 0; cts.Token.IsCancellationRequested == false; i++)
        {
            try { await ingress.Writer.WriteAsync(i, cts.Token); await Task.Delay(10, cts.Token); }
            catch (OperationCanceledException) { break; }
        }
        ingress.Writer.Complete();
    });

    // Worker — the IHostedService.ExecuteAsync shape.
    var worker = Task.Run(async () =>
    {
        long n = 0;
        try
        {
            await foreach (var item in ingress.Reader.ReadAllAsync(cts.Token))
            {
                n++;
                await Task.Delay(5, cts.Token);
            }
        }
        catch (OperationCanceledException) { /* graceful */ }
        Console.WriteLine($"  worker processed {n} items before shutdown");
    });

    await Task.WhenAll(producer, worker);
}

static async Task ShardedPipelineDemo()
{
    const int shards = 4;
    var channels = Enumerable.Range(0, shards).Select(_ => Channel.CreateBounded<int>(64)).ToArray();

    var workers = channels.Select((ch, i) => Task.Run(async () =>
    {
        long sum = 0;
        await foreach (var item in ch.Reader.ReadAllAsync()) sum += item;
        Console.WriteLine($"  shard {i} sum: {sum}");
    })).ToArray();

    // Dispatcher — items are routed to a shard by hash(key)
    for (var i = 0; i < 1000; i++)
    {
        var shard = i % shards;            // stand-in for a real partitioning function
        await channels[shard].Writer.WriteAsync(i);
    }
    foreach (var ch in channels) ch.Writer.Complete();

    await Task.WhenAll(workers);
}
