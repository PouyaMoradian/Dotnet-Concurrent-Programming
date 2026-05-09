namespace Chapter07.Demos;

/// <summary>
/// Demonstrates structured concurrency: when one sibling task fails, all siblings are
/// cancelled cooperatively, and the entire group's exceptions surface together.
/// </summary>
internal static class StructuredConcurrencyDemo
{
    public static async Task Run()
    {
        try
        {
            await RunGroup(async ct =>
            {
                var t1 = WorkAsync("ok-1", 200, ct);
                var t2 = WorkAsync("ok-2", 200, ct);
                var t3 = WorkAsync("fail", 100, ct, throwAt: 100);
                await Task.WhenAll(t1, t2, t3);
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  group failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task RunGroup(Func<CancellationToken, Task> work)
    {
        using var cts = new CancellationTokenSource();
        try
        {
            await work(cts.Token);
        }
        catch
        {
            cts.Cancel();          // cancel any siblings still running
            throw;
        }
    }

    private static async Task WorkAsync(string name, int ms, CancellationToken ct, int? throwAt = null)
    {
        Console.WriteLine($"  start: {name}");
        await Task.Delay(throwAt ?? ms, ct);
        if (throwAt is not null) throw new InvalidOperationException($"{name} blew up");
        Console.WriteLine($"  done : {name}");
    }
}
