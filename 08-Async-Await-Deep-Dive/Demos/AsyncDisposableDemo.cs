namespace Chapter08.Demos;

internal static class AsyncDisposableDemo
{
    public static async Task Run()
    {
        await using (var resource = new FlushableResource())
        {
            await resource.WriteAsync("hello");
            await resource.WriteAsync("world");
        }
        Console.WriteLine("  resource disposed (asynchronously flushed)");
    }

    private sealed class FlushableResource : IAsyncDisposable
    {
        private readonly List<string> _buffer = [];

        public async Task WriteAsync(string s)
        {
            await Task.Delay(10);
            _buffer.Add(s);
        }

        public async ValueTask DisposeAsync()
        {
            // Real version: flush to disk / network. Here just simulate latency.
            await Task.Delay(20);
            Console.WriteLine($"   flushed {_buffer.Count} entries");
        }
    }
}
