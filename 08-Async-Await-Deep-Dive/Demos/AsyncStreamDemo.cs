namespace Chapter08.Demos;

internal static class AsyncStreamDemo
{
    public static async Task Run()
    {
        await foreach (var item in ProduceAsync(5))
            Console.WriteLine($"  consumed: {item}");
    }

    private static async IAsyncEnumerable<int> ProduceAsync(int count, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var i = 0; i < count; i++)
        {
            await Task.Delay(50, ct);
            yield return i;
        }
    }
}
