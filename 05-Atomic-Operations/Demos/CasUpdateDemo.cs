namespace Chapter05.Demos;

/// <summary>
/// Lock-free max() of a stream of values. The CAS retry loop is the canonical
/// shape of lock-free update.
/// </summary>
internal static class CasUpdateDemo
{
    private static long _max = long.MinValue;

    public static async Task Run()
    {
        const int threads = 8;
        const int items = 200_000;
        var rng = new Random(42);
        var input = Enumerable.Range(0, items).Select(_ => (long)rng.Next(int.MaxValue)).ToArray();

        await Task.WhenAll(Enumerable.Range(0, threads).Select(t => Task.Run(() =>
        {
            for (var i = t; i < input.Length; i += threads) UpdateMax(input[i]);
        })));

        var actual = input.Max();
        Console.WriteLine($"  CAS-computed max:    {_max:N0}");
        Console.WriteLine($"  actual max:          {actual:N0}");
        Console.WriteLine($"  match: {_max == actual}");
    }

    private static void UpdateMax(long candidate)
    {
        long current;
        do
        {
            current = Volatile.Read(ref _max);
            if (candidate <= current) return;          // no update needed
        } while (Interlocked.CompareExchange(ref _max, candidate, current) != current);
    }
}
