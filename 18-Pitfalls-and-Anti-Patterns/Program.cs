using Concurrency.Shared;

await ConsoleLab.Run("Chapter 18 — Pitfalls and Anti-Patterns",
[
    ("Deadlock — two locks, opposite order",  DeadlockDemo),
    ("ThreadPool starvation reproducer",      StarvationDemo),
    ("Sync-over-async — illustration",        SyncOverAsyncDemo),
    ("Hidden allocations — LINQ in hot path", HiddenAllocDemo),
    ("Async void — exceptions vanish",        AsyncVoidDemo),
],
args);

static Task DeadlockDemo()
{
    var a = new object();
    var b = new object();

    var t1 = Task.Run(() =>
    {
        lock (a) { Thread.Sleep(50); lock (b) { } }
    });
    var t2 = Task.Run(() =>
    {
        lock (b) { Thread.Sleep(50); lock (a) { } }
    });

    if (Task.WhenAll(t1, t2).Wait(TimeSpan.FromSeconds(2)))
        Console.WriteLine("  no deadlock observed (lucky scheduling)");
    else
        Console.WriteLine("  DEADLOCK detected (timed out). Fix: always lock in a consistent order.");

    return Task.CompletedTask;
}

static async Task StarvationDemo()
{
    ThreadPool.SetMinThreads(Environment.ProcessorCount, Environment.ProcessorCount);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var outer = Enumerable.Range(0, 30).Select(_ => Task.Run(() =>
    {
        // sync-over-async: blocks the worker.
        Task.Run(() => Thread.Sleep(100)).Wait();
    }));
    await Task.WhenAll(outer);
    sw.Stop();
    Console.WriteLine($"  starvation pattern (sync-wait inside Task.Run): {sw.ElapsedMilliseconds} ms");
    Console.WriteLine("  Without hill-climbing's slow ramp this would be ~100 ms; with starvation it's 200-400 ms.");
}

static Task SyncOverAsyncDemo()
{
    Console.WriteLine("  In ASP.NET Core or console hosts, sync-over-async slows down but doesn't usually deadlock.");
    Console.WriteLine("  In legacy ASP.NET / WinForms / WPF (with SynchronizationContext), it deadlocks.");
    Console.WriteLine("  See 08-Async-Await-Deep-Dive/SynchronizationContext for the mechanism.");
    return Task.CompletedTask;
}

static Task HiddenAllocDemo()
{
    var data = Enumerable.Range(0, 1_000_000).ToArray();

    // BAD: chains of LINQ allocate enumerators / closures.
    long s1 = 0;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    foreach (var x in data.Where(x => x % 2 == 0).Select(x => x * 2)) s1 += x;
    sw.Stop();
    Console.WriteLine($"  LINQ chain:  {sw.ElapsedMilliseconds} ms  (sum={s1})");

    // GOOD: tight loop.
    long s2 = 0;
    sw.Restart();
    for (var i = 0; i < data.Length; i++) if ((data[i] & 1) == 0) s2 += data[i] * 2;
    sw.Stop();
    Console.WriteLine($"  Tight loop:  {sw.ElapsedMilliseconds} ms  (sum={s2})");
    return Task.CompletedTask;
}

static async Task AsyncVoidDemo()
{
    Console.WriteLine("  An async-void method:");
    Console.WriteLine();
    Console.WriteLine("    static async void Boom() { await Task.Delay(10); throw new Exception(\"oops\"); }");
    Console.WriteLine();
    Console.WriteLine("  …throws on the SynchronizationContext (or ThreadPool). The caller can't catch it.");
    Console.WriteLine("  Process termination is the default. Always async Task — never async void —");
    Console.WriteLine("  except for legitimate event handlers (Click, etc.).");
    await Task.CompletedTask;
}
