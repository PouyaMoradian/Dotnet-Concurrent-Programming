namespace Chapter01.Demos;

/// <summary>
/// Demonstrates what a <em>torn read</em> is and why <see cref="Interlocked.Read(ref long)"/> prevents it.
///
/// Important framing: on 64-bit .NET, an ordinary read of a properly aligned <c>long</c> is already
/// atomic (ECMA-335 §I.12.6.6), so you cannot make a plain aligned <c>long</c> tear on a modern x64/ARM64
/// runtime — and this demo does not pretend otherwise. Tearing happens when a 64-bit value is published as
/// two separate 32-bit operations. That is exactly what a 32-bit runtime does for <em>every</em> <c>long</c>,
/// and it is what happens any time code hand-splits a wide value into independent writes.
///
/// We model that explicitly: the "unsafe" path stores the value as two separate 32-bit halves, so a reader
/// can observe the low half of one value spliced onto the high half of another. The "safe" path stores and
/// loads the whole 64-bit value atomically with <see cref="Interlocked"/> and never tears.
/// </summary>
internal static class TornLongReadDemo
{
    // A 64-bit value modelled as two independent 32-bit halves. Writing/reading these separately is
    // precisely the non-atomic publication that a 32-bit runtime performs for an ordinary `long`.
    private static int _lo;
    private static int _hi;

    // A properly aligned 64-bit field used for the always-correct atomic path.
    private static long _aligned;

    public static async Task Run()
    {
        const long patternA = 0x0000_0000_FFFF_FFFFL;            // hi = 0x00000000, lo = 0xFFFFFFFF
        const long patternB = unchecked((long)0xFFFF_FFFF_0000_0000L); // hi = 0xFFFFFFFF, lo = 0x00000000

        await DemonstrateTearing(patternA, patternB);
        await DemonstrateSafePath(patternA, patternB);

        Console.WriteLine();
        Console.WriteLine("  An aligned 64-bit `long` read is already atomic on 64-bit .NET — it cannot tear.");
        Console.WriteLine("  Tearing appears when a wide value is published as multiple operations:");
        Console.WriteLine("  every `long` on a 32-bit runtime, or any hand-split read/write like the one above.");
        Console.WriteLine("  Interlocked.Read/Exchange (or Volatile on aligned word-sized values) is the portable fix.");
    }

    // Splits the 64-bit write into two 32-bit stores, so a concurrent reader can splice mismatched halves.
    private static async Task DemonstrateTearing(long patternA, long patternB)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var torn = 0L;
        var loops = 0L;

        // One writer alternating between the two patterns each iteration. Volatile stores keep the JIT from
        // hoisting the writes out of the loop and force them to memory between the two halves.
        var writer = Task.Run(() =>
        {
            var useA = true;
            while (!cts.IsCancellationRequested)
            {
                var value = useA ? patternA : patternB;
                Volatile.Write(ref _lo, (int)value);             // publish low half...
                Volatile.Write(ref _hi, (int)(value >> 32));     // ...then high half (a torn window opens here)
                useA = !useA;
            }
        }, cts.Token);

        var reader = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                int lo = Volatile.Read(ref _lo);
                int hi = Volatile.Read(ref _hi);
                long v = ((long)hi << 32) | (uint)lo;
                loops++;
                if (v != patternA && v != patternB)
                    Interlocked.Increment(ref torn);
            }
        }, cts.Token);

        try { await Task.WhenAll(writer, reader); }
        catch (OperationCanceledException) { /* expected */ }

        Console.WriteLine($"  split 32-bit halves (models a non-atomic long): torn observations = {torn:N0} (out of {loops:N0} reads)");
    }

    private static async Task DemonstrateSafePath(long patternA, long patternB)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var torn = 0L;
        var loops = 0L;

        var w1 = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested) Interlocked.Exchange(ref _aligned, patternA);
        }, cts.Token);

        var w2 = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested) Interlocked.Exchange(ref _aligned, patternB);
        }, cts.Token);

        var r = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                var v = Interlocked.Read(ref _aligned);          // atomic 64-bit load
                loops++;
                if (v != patternA && v != patternB)
                    Interlocked.Increment(ref torn);
            }
        }, cts.Token);

        try { await Task.WhenAll(w1, w2, r); }
        catch (OperationCanceledException) { /* expected */ }

        Console.WriteLine($"  aligned Interlocked.Read: torn observations = {torn:N0} (out of {loops:N0} reads)");
    }
}
