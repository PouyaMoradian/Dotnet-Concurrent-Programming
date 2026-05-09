using System.Threading.Channels;

namespace Chapter09.Demos;

internal static class UnboundedDemo
{
    public static async Task Run()
    {
        var ch = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });

        var producer = Task.Run(async () =>
        {
            for (var i = 0; i < 1_000_000; i++)
            {
                await ch.Writer.WriteAsync(i);
            }
            ch.Writer.Complete();
        });

        long n = 0;
        await foreach (var _ in ch.Reader.ReadAllAsync()) n++;
        await producer;

        Console.WriteLine($"  consumed {n:N0} items via unbounded channel");
        Console.WriteLine();
        Console.WriteLine("  Unbounded works *here* because we drain it before more arrives. In production,");
        Console.WriteLine("  unbounded + slow consumer = OOM. Default to bounded; opt-in to unbounded only");
        Console.WriteLine("  when the producer is itself rate-limited.");
    }
}
