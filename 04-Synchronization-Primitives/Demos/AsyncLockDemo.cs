namespace Chapter04.Demos;

/// <summary>
/// You cannot 'lock(...) await ...' — the C# compiler refuses because the lock would
/// be held by *some* thread but the awaiter could resume on a different one.
/// The standard async-correct mutex is SemaphoreSlim with WaitAsync(1,1).
/// </summary>
internal static class AsyncLockDemo
{
    private static readonly SemaphoreSlim Mutex = new(1, 1);
    private static int _counter;

    public static async Task Run()
    {
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            for (var i = 0; i < 100; i++)
            {
                await Mutex.WaitAsync();
                try
                {
                    _counter++;
                    await Task.Delay(1);     // an await *inside* the critical section is fine here
                }
                finally
                {
                    Mutex.Release();
                }
            }
        }));

        Console.WriteLine($"  counter (expected 800): {_counter}");
    }
}
