namespace Chapter01.Demos;

/// <summary>
/// Demonstrates that "thread A wrote, thread B reads" doesn't guarantee B sees A's write
/// without explicit ordering. We can't reliably *demonstrate* the bug on x86 (TSO is forgiving)
/// but we can show the technique — and on ARM64 the unprotected version actually fails.
/// </summary>
internal static class MemoryVisibilityDemo
{
    private static int _data;
    private static bool _ready;

    public static async Task Run()
    {
        // Reset.
        _data = 0;
        _ready = false;

        var consumer = Task.Run(() =>
        {
            // Spin until we see the flag. WITHOUT volatile/Interlocked this could spin forever
            // on architectures where the JIT hoists the read into a register, or the store buffer
            // delays visibility.
            while (!Volatile.Read(ref _ready)) Thread.SpinWait(8);
            return Volatile.Read(ref _data);
        });

        await Task.Delay(50);                    // give consumer a head start so it's spinning
        Volatile.Write(ref _data, 42);           // 1) publish the value
        Volatile.Write(ref _ready, true);        // 2) publish the flag — Volatile.Write is release semantics

        var observed = await consumer;
        Console.WriteLine($"  consumer observed _data = {observed}  (expected 42)");

        Console.WriteLine();
        Console.WriteLine("  The two Volatile.Writes establish a release; the Volatile.Reads in the consumer");
        Console.WriteLine("  establish acquires. The release-acquire pair is what guarantees that observing");
        Console.WriteLine("  '_ready == true' implies '_data == 42' is visible. See Chapter 12.");
    }
}
