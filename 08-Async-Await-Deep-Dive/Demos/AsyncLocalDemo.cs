namespace Chapter08.Demos;

internal static class AsyncLocalDemo
{
    private static readonly AsyncLocal<string> _correlationId = new();
    [ThreadStatic] private static string? _threadStatic;

    public static async Task Run()
    {
        _correlationId.Value = "req-42";
        _threadStatic = "tls-42";

        Console.WriteLine($"  before await:  AsyncLocal='{_correlationId.Value}'  ThreadStatic='{_threadStatic}'");
        await Task.Delay(50);
        Console.WriteLine($"  after  await:  AsyncLocal='{_correlationId.Value}'  ThreadStatic='{_threadStatic}'");

        // AsyncLocal flows; ThreadStatic doesn't (we likely resumed on a different worker).
        await Task.Run(async () =>
        {
            Console.WriteLine($"  in Task.Run:  AsyncLocal='{_correlationId.Value}'  ThreadStatic='{_threadStatic}'");
            await Task.Yield();
            Console.WriteLine($"  after yield in Task.Run: AsyncLocal='{_correlationId.Value}'  ThreadStatic='{_threadStatic}'");
        });

        Console.WriteLine();
        Console.WriteLine("  AsyncLocal<T> is the right tool for request-scoped data (correlation id,");
        Console.WriteLine("  current user, OpenTelemetry activity context).");
    }
}
