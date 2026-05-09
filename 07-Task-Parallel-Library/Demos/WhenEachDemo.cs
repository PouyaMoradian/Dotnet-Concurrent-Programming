namespace Chapter07.Demos;

internal static class WhenEachDemo
{
    public static async Task Run()
    {
        // Several tasks finishing at different times; we want results streamed in completion order.
        var tasks = new[]
        {
            DelayValue(300, "third"),
            DelayValue(100, "first"),
            DelayValue(200, "second"),
        };

        await foreach (var t in Task.WhenEach(tasks))
        {
            Console.WriteLine($"  arrived: {await t}");      // never blocks; t already complete
        }

        Console.WriteLine();
        Console.WriteLine("  WhenEach is .NET 9; pre-9 you build the same with a Channel<Task>");
        Console.WriteLine("  and Task.ContinueWith.");
    }

    private static async Task<string> DelayValue(int ms, string val)
    {
        await Task.Delay(ms);
        return val;
    }
}
