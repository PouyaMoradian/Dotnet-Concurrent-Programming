namespace Chapter07.Demos;

internal static class WhenAllExceptionsDemo
{
    public static async Task Run()
    {
        var tasks = new[]
        {
            Task.Run(() => { throw new InvalidOperationException("first"); }),
            Task.Run(() => { throw new ArgumentException("second"); }),
            Task.Run(() => Task.Delay(10))
        };

        try { await Task.WhenAll(tasks); }
        catch (Exception ex)
        {
            Console.WriteLine($"  await rethrew: {ex.GetType().Name}: {ex.Message}");
        }

        // To see all faults, inspect each task afterwards.
        var faults = tasks.Where(t => t.IsFaulted)
                          .SelectMany(t => t.Exception!.InnerExceptions)
                          .ToList();
        Console.WriteLine($"  total inner exceptions: {faults.Count}");
        foreach (var f in faults) Console.WriteLine($"    - {f.GetType().Name}: {f.Message}");
    }
}
