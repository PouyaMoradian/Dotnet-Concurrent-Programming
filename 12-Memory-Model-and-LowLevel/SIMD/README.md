# SIMD with `Vector<T>`

`System.Numerics.Vector<T>` is a runtime-sized vector type that the JIT lowers to the widest SIMD instructions the host supports. It's the simplest entry to data-parallel compute in .NET.

## Hello, world

```csharp
public static int Sum(int[] data)
{
    var width = Vector<int>.Count;
    var i = 0;
    var acc = Vector<int>.Zero;
    var span = data.AsSpan();
    for (; i + width <= data.Length; i += width)
        acc += new Vector<int>(span.Slice(i, width));

    var total = 0;
    for (var k = 0; k < width; k++) total += acc[k];
    for (; i < data.Length; i++) total += data[i];
    return total;
}
```

`Vector<T>.Count` is constant per process (the JIT chose) — typically 8 on AVX2 x86, 16 on AVX-512, 4 on AdvSimd ARM64. The horizontal add uses indexed access; for ≥ .NET 8, prefer `Vector.Sum(acc)`.

## When to use Vector vs Vector256/Vector512

| Type | Best for |
|---|---|
| `Vector<T>` | "Just give me the widest available." Simple, portable. |
| `Vector128<T>` / `Vector256<T>` / `Vector512<T>` | Explicit width. Useful when you have specific shuffles/masks for 128 vs 256. |
| Architecture intrinsics (`Avx2.*`, `AdvSimd.*`) | When you need an instruction `Vector*` doesn't expose. |

For most numeric loops, `Vector<T>` with `Vector.Sum` / `Vector.Multiply` etc. is enough.

## Autovectorisation

.NET 8+ autovectorises many simple loops without intrinsics:

```csharp
var sum = 0;
for (var i = 0; i < data.Length; i++) sum += data[i];
```

The JIT may emit SSE/AVX. Verify with `[DisassemblyDiagnoser]` — if you see `xmmN`/`ymmN` registers, vectorisation kicked in. If not, hand-roll with `Vector<T>`.

## Concurrency angle

A `Parallel.For` over partitions, where each partition's body is SIMD-vectorised, is **the** combination. The outer parallel multiplies cores; the inner SIMD multiplies per-core throughput. Done right, you can outperform single-threaded scalar by 30–60×.

```csharp
Parallel.For(0, partitions, p =>
{
    var (start, end) = Partition(p);
    long local = 0;
    var width = Vector<int>.Count;
    var i = start;
    var acc = Vector<int>.Zero;
    for (; i + width <= end; i += width) acc += new Vector<int>(data.AsSpan(i, width));
    for (var k = 0; k < width; k++) local += acc[k];
    for (; i < end; i++) local += data[i];
    Interlocked.Add(ref total, local);
});
```

## Gotchas

- **Allocations of `new Vector<T>(span)`** can be expensive on hot loops; prefer `MemoryMarshal.Cast<byte, Vector<int>>(...)` for in-place reading. Or just trust the JIT — recent versions inline `new Vector` to a load.
- **Not all element types are supported.** `Vector<long>` works, `Vector<DateTime>` doesn't. Stick to numeric primitives.
- **Avoid scalar fallbacks where possible.** Branching to scalar from vector code costs a vector unit reset.
