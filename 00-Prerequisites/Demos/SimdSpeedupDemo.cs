using System.Diagnostics;
using System.Numerics;

namespace Chapter00.Demos;

/// <summary>
/// Sums a float array two ways: a scalar loop, and a portable <see cref="Vector{T}"/>
/// loop. On a host with NEON (Apple Silicon, ARM64) <c>Vector&lt;float&gt;.Count</c> is 4;
/// on AVX2 it is 8; on AVX-512 it is 16. The vector loop runs one ADD per <c>Count</c>
/// elements instead of one ADD per element. .NET 8+ will also autovectorise the scalar
/// loop in many cases, narrowing the gap — read the disassembly with DOTNET_JitDisasm
/// to see what happened.
/// </summary>
internal static class SimdSpeedupDemo
{
    public static Task Run()
    {
        const int n = 16 * 1024 * 1024;     // 64 MB of floats
        const int repeats = 4;

        var data = new float[n];
        for (var i = 0; i < n; i++) data[i] = i * 0.5f;

        // Warm up.
        _ = SumScalar(data);
        _ = SumVector(data);

        var scalarMs = Time(() => SumScalar(data), repeats);
        var vectorMs = Time(() => SumVector(data), repeats);

        Console.WriteLine($"  elements: {n:N0}   repeats: {repeats}");
        Console.WriteLine($"  Vector<float>.Count on this host: {Vector<float>.Count}");
        Console.WriteLine($"  Vector.IsHardwareAccelerated:    {Vector.IsHardwareAccelerated}");
        Console.WriteLine();
        Console.WriteLine($"  scalar loop:       {scalarMs,6} ms");
        Console.WriteLine($"  Vector<float> loop:{vectorMs,6} ms");
        Console.WriteLine();
        var ratio = vectorMs == 0 ? double.NaN : (double)scalarMs / Math.Max(1, vectorMs);
        Console.WriteLine($"  scalar / vector ratio: {ratio:F2}×");
        Console.WriteLine();
        Console.WriteLine("  Expected: ~4× on NEON, ~6-8× on AVX2, ~12-16× on AVX-512.");
        Console.WriteLine("  Lower than expected? .NET 8+ may have autovectorised the scalar loop —");
        Console.WriteLine("  check the JIT disasm to confirm. Or the workload may be memory-bandwidth bound.");
        return Task.CompletedTask;
    }

    private static double Time(Func<float> work, int repeats)
    {
        var sw = Stopwatch.StartNew();
        float acc = 0;
        for (var r = 0; r < repeats; r++) acc += work();
        sw.Stop();
        if (acc == float.MinValue) Console.WriteLine();   // anti-elision
        return sw.ElapsedMilliseconds;
    }

    private static float SumScalar(float[] data)
    {
        float sum = 0;
        for (var i = 0; i < data.Length; i++) sum += data[i];
        return sum;
    }

    private static float SumVector(float[] data)
    {
        var i = 0;
        var width = Vector<float>.Count;
        var vsum = Vector<float>.Zero;
        var span = data.AsSpan();
        for (; i <= span.Length - width; i += width)
            vsum += new Vector<float>(span.Slice(i, width));

        var sum = Vector.Sum(vsum);
        for (; i < span.Length; i++) sum += span[i];
        return sum;
    }
}
