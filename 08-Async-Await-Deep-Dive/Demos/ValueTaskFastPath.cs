namespace Chapter08.Demos;

internal static class ValueTaskFastPath
{
    private static readonly Dictionary<int, int> Cache = Enumerable.Range(0, 100).ToDictionary(i => i, i => i * i);
    private static int _slowHits;

    public static async Task Run()
    {
        long total = 0;
        for (var i = 0; i < 100_000; i++) total += await SquareAsync(i % 200);
        Console.WriteLine($"  total: {total}");
        Console.WriteLine($"  slow-path hits: {_slowHits}/200000  — fast-path returns ValueTask synchronously, no allocation.");
    }

    private static ValueTask<int> SquareAsync(int x)
    {
        if (Cache.TryGetValue(x, out var v)) return new ValueTask<int>(v);
        return new ValueTask<int>(SlowAsync(x));
    }

    private static async Task<int> SlowAsync(int x)
    {
        Interlocked.Increment(ref _slowHits);
        await Task.Yield();
        return x * x;
    }
}
