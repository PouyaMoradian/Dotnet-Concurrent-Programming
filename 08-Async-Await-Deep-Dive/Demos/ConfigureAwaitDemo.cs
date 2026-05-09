namespace Chapter08.Demos;

internal static class ConfigureAwaitDemo
{
    public static async Task Run()
    {
        Console.WriteLine($"  current SynchronizationContext: {(SynchronizationContext.Current?.ToString() ?? "null")}");
        await Task.Delay(50).ConfigureAwait(false);
        Console.WriteLine("  after ConfigureAwait(false): no observable difference on console — there's nothing to capture.");

        // ConfigureAwaitOptions (.NET 8): more knobs.
        await Task.Delay(50).ConfigureAwait(ConfigureAwaitOptions.None);

        var failing = Task.Run(() => { throw new InvalidOperationException("boom"); });
        // SuppressThrowing (.NET 8): await without rethrow.
        await failing.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        Console.WriteLine($"  failing task status (after SuppressThrowing await): {failing.Status}");
    }
}
