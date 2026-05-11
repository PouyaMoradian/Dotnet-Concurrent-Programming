using System.Runtime.InteropServices;

namespace Chapter01.Demos;

/// <summary>
/// Shows that an ordinary <c>long</c> read concurrent with writes can — under the right
/// alignment and architecture — observe a value that was never actually written. On 64-bit
/// .NET with default 8-byte alignment, ordinary long loads/stores are atomic; misalignment
/// breaks that guarantee on x86-64 and is explicitly illegal on ARM64 (it raises
/// <see cref="DataMisalignedException"/>). The portable, always-correct fix is
/// <see cref="Interlocked.Read(ref long)"/> on a properly-aligned field.
/// </summary>
internal static class TornLongReadDemo
{
    // Properly aligned long, used to demonstrate the safe pattern on every architecture.
    private static long _aligned;

    // Deliberately misaligned long, used to demonstrate the failure mode on x86-64. On ARM64
    // touching this with an unprotected read/write will fault; we detect the architecture and
    // skip that half of the demo there.
    [StructLayout(LayoutKind.Explicit)]
    private struct Misaligned
    {
        [FieldOffset(4)] public long Value;
    }

    private static Misaligned _shared;

    public static async Task Run()
    {
        const long patternA = 0x0000_0000_FFFF_FFFFL;
        const long patternB = unchecked((long)0xFFFF_FFFF_0000_0000L);
        var arch = RuntimeInformation.ProcessArchitecture;

        if (arch is Architecture.X64 or Architecture.X86)
        {
            await DemonstrateTearing(patternA, patternB);
        }
        else
        {
            Console.WriteLine($"  ({arch}) — misaligned long access faults on this architecture;");
            Console.WriteLine("   skipping the tearing demo. The safe-path demo below still runs.");
        }

        await DemonstrateSafePath(patternA, patternB);

        Console.WriteLine();
        Console.WriteLine("  On 64-bit .NET with default alignment, ordinary long reads ARE atomic.");
        Console.WriteLine("  Misalignment removes that guarantee (tearing on x86, fault on ARM).");
        Console.WriteLine("  Interlocked.Read on an aligned field is atomic on every architecture.");
    }

    private static async Task DemonstrateTearing(long patternA, long patternB)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var torn = 0L;
        var loops = 0L;

        var writerA = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested) _shared.Value = patternA;
        }, cts.Token);

        var writerB = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested) _shared.Value = patternB;
        }, cts.Token);

        var reader = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                var v = _shared.Value;                           // possibly torn
                loops++;
                if (v != patternA && v != patternB)
                    Interlocked.Increment(ref torn);
            }
        }, cts.Token);

        try { await Task.WhenAll(writerA, writerB, reader); }
        catch (OperationCanceledException) { /* expected */ }

        Console.WriteLine($"  misaligned ordinary read: torn observations = {torn:N0} (out of {loops:N0} reads)");
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
                var v = Interlocked.Read(ref _aligned);          // atomic
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
