using Concurrency.Shared;

await ConsoleLab.Run("Chapter 13 — Cancellation and Coordination",
[
    ("Cancel a Task.Delay-based loop", async () =>
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        try
        {
            for (var i = 0; i < 10; i++)
            {
                await Task.Delay(50, cts.Token);
                Console.WriteLine($"  iteration {i}");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("  cancelled cleanly");
        }
    }),
    ("Linked tokens (timeout + outer)", async () =>
    {
        using var outer = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(outer.Token, timeout.Token);
        try { await Task.Delay(1000, linked.Token); }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"  timeout? {timeout.IsCancellationRequested}  outer? {outer.IsCancellationRequested}");
        }
    }),
    ("Wait-Async with timeout (.NET 6+)", async () =>
    {
        var slow = Task.Delay(1000);
        try
        {
            await slow.WaitAsync(TimeSpan.FromMilliseconds(100));
        }
        catch (TimeoutException)
        {
            Console.WriteLine("  TimeoutException — Task.WaitAsync gave up; the slow task is still running");
        }
    }),
],
args);
