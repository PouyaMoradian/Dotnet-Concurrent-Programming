# Hardware intrinsics

`System.Runtime.Intrinsics.*` exposes per-architecture CPU instructions directly. The .NET 8/9/10 JIT recognises these as intrinsic — they emit the corresponding instruction with no overhead.

## Namespaces

- `System.Runtime.Intrinsics.X86` — SSE / SSE2 / SSE3 / SSE4 / AVX / AVX2 / AVX-512 / BMI1 / BMI2 / etc.
- `System.Runtime.Intrinsics.Arm` — AdvSimd, Aes, Crc32, Sha1, Sha256, Sve.
- Cross-platform: `Vector128<T>`, `Vector256<T>`, `Vector512<T>` (.NET 8+) — write once; the JIT picks the best per-architecture instructions.

## Capability check

Always gate on `IsSupported` on a per-instruction-set basis:

```csharp
if (Avx2.IsSupported) { /* AVX2 path */ }
else if (Sse42.IsSupported) { /* SSE4.2 path */ }
else { /* scalar fallback */ }
```

`IsSupported` is a JIT-time constant — the JIT eliminates the branch entirely for the path it chose.

## Cross-platform vector

```csharp
public static int Sum(ReadOnlySpan<int> data)
{
    int i = 0, total = 0;
    if (Vector256<int>.IsSupported && data.Length >= Vector256<int>.Count)
    {
        var acc = Vector256<int>.Zero;
        for (; i + Vector256<int>.Count <= data.Length; i += Vector256<int>.Count)
            acc += Vector256.Create(data.Slice(i, Vector256<int>.Count));

        // horizontal add
        total = Vector256.Sum(acc);
    }
    for (; i < data.Length; i++) total += data[i];
    return total;
}
```

This is the modern shape. On x86-64 with AVX2 → 8 ints per iteration. On ARM64 with AdvSimd → 4 ints per iteration. Same source.

## Concurrency relevance

Intrinsics are sequential within a thread. Multiple threads each running vectorised code parallelise across cores naturally. The intersection with concurrency:

- **Atomic on a vector?** No — there's no atomic 256-bit load/store. For shared vector data, copy under a lock or use compare-exchange on a 128-bit struct (CAS16).
- **Memory ordering?** Intrinsics inherit normal load/store ordering. No special acquire/release on `Vector256.LoadAligned`.
- **Cache effects?** Vector loads pull entire cache lines; if they straddle lines (unaligned), you get two cache misses instead of one. Use `LoadAligned` when possible.

## When to reach for intrinsics

- You wrote it in scalar form, profiled, and found a hot loop that's vector-amenable.
- `Vector<T>` autovectorisation already helps but the compiler isn't smart enough for your shape.
- You're writing high-performance crypto, compression, parsing, or scientific compute.

For ordinary application code, **don't**. The .NET 8+ JIT autovectorises straightforward loops. `LINQ.Sum` over a `int[]` already uses SIMD internally as of .NET 9.
