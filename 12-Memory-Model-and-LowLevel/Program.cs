using System.Numerics;
using System.Runtime.Intrinsics;
using Concurrency.Shared;

await ConsoleLab.Run("Chapter 12 — Memory Model and Low-Level",
[
    ("SIMD vector sum (Vector<T>)", () =>
    {
        const int n = 1 << 20;
        var data = new int[n];
        for (var i = 0; i < n; i++) data[i] = 1;

        long s = 0;
        var width = Vector<int>.Count;
        var span = data.AsSpan();
        var i2 = 0;
        var vsum = Vector<int>.Zero;
        for (; i2 + width <= n; i2 += width) vsum += new Vector<int>(span.Slice(i2, width));
        for (var j = 0; j < width; j++) s += vsum[j];
        for (; i2 < n; i2++) s += data[i2];

        Console.WriteLine($"  width = {width} ints per vector");
        Console.WriteLine($"  sum   = {s} (expected {n})");
        return Task.CompletedTask;
    }),
    ("Vector256<T> direct intrinsics check", () =>
    {
        Console.WriteLine($"  Vector256<int>.IsSupported : {Vector256<int>.IsSupported}");
        Console.WriteLine($"  Vector512<int>.IsSupported : {Vector512<int>.IsSupported}");
        Console.WriteLine($"  Sse42.IsSupported          : {System.Runtime.Intrinsics.X86.Sse42.IsSupported}");
        Console.WriteLine($"  Avx2.IsSupported           : {System.Runtime.Intrinsics.X86.Avx2.IsSupported}");
        Console.WriteLine($"  Avx512F.IsSupported        : {System.Runtime.Intrinsics.X86.Avx512F.IsSupported}");
        Console.WriteLine($"  AdvSimd.IsSupported        : {System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported}");
        return Task.CompletedTask;
    }),
],
args);
