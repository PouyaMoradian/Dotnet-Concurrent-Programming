namespace Chapter07.Demos;

/// <summary>
/// ValueTask gets you allocation-free async when results are usually available synchronously.
/// </summary>
internal static class ValueTaskDemo
{
    private static int _hits;
    private static readonly Dictionary<string, byte[]> Cache = new()
    {
        ["a"] = [1, 2, 3],
        ["b"] = [4, 5, 6],
    };

    public static async Task Run()
    {
        var keys = new[] { "a", "b", "a", "c", "b", "a" };

        long sum = 0;
        foreach (var key in keys)
        {
            sum += (await GetAsync(key)).Length;
        }
        Console.WriteLine($"  sum of byte counts: {sum}");
        Console.WriteLine($"  cache hits (sync-completion): {_hits}/{keys.Length}");
        Console.WriteLine();
        Console.WriteLine("  Each cache hit returned ValueTask synchronously — no Task allocation.");
        Console.WriteLine("  Each miss took the async slow path, allocating only when necessary.");
    }

    private static ValueTask<byte[]> GetAsync(string key)
    {
        if (Cache.TryGetValue(key, out var v))
        {
            _hits++;
            return new ValueTask<byte[]>(v);             // sync completion, zero allocation
        }
        return new ValueTask<byte[]>(SlowAsync(key));    // wraps a Task; rare path
    }

    private static async Task<byte[]> SlowAsync(string key)
    {
        await Task.Delay(10);
        return new byte[8];
    }
}
