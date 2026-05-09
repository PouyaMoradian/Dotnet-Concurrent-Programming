namespace Chapter01.Demos;

/// <summary>
/// The textbook race: 8 threads each increment a shared int 1,000,000 times.
/// Expected total is 8,000,000. The unguarded version is *systematically* wrong
/// because <c>i++</c> is read-modify-write, not atomic.
/// </summary>
internal static class MutableStateRace
{
    public static async Task Run()
    {
        const int threads = 8;
        const int iterations = 1_000_000;

        // Unsynchronised — racy.
        var racy = 0;
        await Task.WhenAll(Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < iterations; i++) racy++;
        })));
        Console.WriteLine($"  unsynchronised int++:    expected {threads * iterations:N0}, got {racy:N0}  (lost { (threads * iterations) - racy:N0})");

        // Interlocked — atomic.
        var atomic = 0;
        await Task.WhenAll(Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < iterations; i++) Interlocked.Increment(ref atomic);
        })));
        Console.WriteLine($"  Interlocked.Increment:   expected {threads * iterations:N0}, got {atomic:N0}");

        // Lock — also correct, slower under contention.
        var locked = 0;
        var sync = new object();
        await Task.WhenAll(Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < iterations; i++)
                lock (sync) locked++;
        })));
        Console.WriteLine($"  lock(object) + ++:       expected {threads * iterations:N0}, got {locked:N0}");

        Console.WriteLine();
        Console.WriteLine("  The race always *loses* updates; it never gains them. That asymmetry is the");
        Console.WriteLine("  signature of write-after-write contention without ordering.");
    }
}
