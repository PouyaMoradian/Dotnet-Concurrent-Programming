using System.Threading.Channels;

namespace Chapter09.Demos;

/// <summary>
/// Three-stage pipeline: source → parse → transform → sink.
/// Each stage is its own task with its own channel input.
/// </summary>
internal static class PipelineDemo
{
    public static async Task Run()
    {
        var raw = Channel.CreateBounded<string>(64);
        var parsed = Channel.CreateBounded<int>(64);

        var source = Task.Run(async () =>
        {
            for (var i = 0; i < 100; i++) await raw.Writer.WriteAsync($"line {i}");
            raw.Writer.Complete();
        });

        var parser = Task.Run(async () =>
        {
            await foreach (var s in raw.Reader.ReadAllAsync())
            {
                var n = int.Parse(s.Split(' ')[1]);
                await parsed.Writer.WriteAsync(n * 2);
            }
            parsed.Writer.Complete();
        });

        long total = 0;
        var sink = Task.Run(async () =>
        {
            await foreach (var n in parsed.Reader.ReadAllAsync()) total += n;
        });

        await Task.WhenAll(source, parser, sink);
        Console.WriteLine($"  pipeline output sum: {total}  (expected {Enumerable.Range(0, 100).Sum(i => i * 2)})");
    }
}
