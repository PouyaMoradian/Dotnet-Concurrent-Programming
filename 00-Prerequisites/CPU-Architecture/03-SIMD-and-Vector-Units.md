# SIMD and vector units

SIMD — Single Instruction, Multiple Data — means doing the *same* operation on a vector of values in one instruction. It is the cheapest way to multiply throughput on numerical or bytewise-uniform code, because the silicon was going to be running the ALU pipeline at full clock anyway; widening the lane width turns one ADD into 4 or 8 or 16 ADDs for the same cycle.

For .NET developers, SIMD shows up in three increasingly explicit ways: autovectorisation by the JIT, the portable `System.Numerics.Vector<T>` API, and the architecture-specific hardware intrinsics under `System.Runtime.Intrinsics.X86` and `System.Runtime.Intrinsics.Arm`.

## The vector ISAs that matter

| Architecture | Extension | Vector width | Year | Notes |
|---|---|---|---|---|
| x86 | SSE2 (Streaming SIMD Extensions 2) | 128 b | 2001 | Baseline of every 64-bit Windows. Always available. |
| x86 | SSE4.1 / SSE4.2 | 128 b | 2007–2008 | String/byte search instructions (PCMPISTRI etc.) |
| x86 | AVX (Advanced Vector Extensions) | 256 b (float only) | 2011 | Wider but integer ops still on 128 b. |
| x86 | AVX2 | 256 b (int + float) | 2013 | The first practical "wide" target. |
| x86 | AVX-512 | 512 b | 2016–present | Lots of sub-extensions; not on consumer Alder/Raptor Lake. |
| ARM | NEON (ARM's 128-bit SIMD extension; not an acronym) | 128 b | 2005 | Baseline of every ARM64. |
| ARM | SVE / SVE2 (Scalable Vector Extension) | scalable (128–2048 b) | 2016–present | Length-agnostic; vector length is a runtime quantity. |

On a desktop in 2026 you can assume AVX2 widely. AVX-512 is server-typical (Sapphire Rapids, EPYC Genoa) and laptop-spotty. On ARM, NEON is everywhere; SVE2 is on server cores like Neoverse V2. Apple's M-series stays on 128-bit NEON for general-purpose SIMD — the M4 added SME/SME2 (the Scalable Matrix Extension, used in a special streaming mode), not user-mode SVE2.

## What the .NET runtime exposes

### Layer 1 — autovectorisation

The JIT's tiered compiler will vectorise straightforward loops on .NET 7+, more aggressively on .NET 8+. The patterns it handles best:

- Sums, dot products, min/max over `Span<int>`/`Span<float>`/...
- Simple element-wise transforms with no branches inside the loop body.
- `MemoryExtensions` and `Tensor` BCL methods are already SIMD-internally.

Patterns that *defeat* autovectorisation:

- Loops with side-effects to non-array memory.
- Branches inside the loop body.
- Loops over non-`Span`/non-array sequences (`IEnumerable<T>`, `IList<T>`).
- Indirection (`array[indexes[i]]`) — though .NET 8+ improved gather/scatter handling.

You can confirm whether the JIT vectorised by reading the disassembly ([05-Reading-JIT-Disassembly.md](05-Reading-JIT-Disassembly.md)) and looking for `vaddps`, `vpaddd`, etc.

### Layer 2 — portable `Vector<T>`

```csharp
using System.Numerics;

public static long Sum(ReadOnlySpan<int> data)
{
    long sum = 0;
    int i = 0;
    if (Vector.IsHardwareAccelerated)
    {
        var vsum = Vector<int>.Zero;
        for (; i <= data.Length - Vector<int>.Count; i += Vector<int>.Count)
            vsum += new Vector<int>(data[i..]);
        sum = Vector.Sum(vsum);  // .NET 6+: lane-wise reduction
    }
    for (; i < data.Length; i++) sum += data[i];
    return sum;
}
```

`Vector<T>.Count` is decided at JIT time based on the host CPU. On a NEON box it's 4 for `int`; on AVX2 it's 8. Note that `Vector<T>` is **capped at 256-bit** in current .NET (so `Vector<int>.Count` stays 8 even on AVX-512 hardware) — to get 512-bit width you must use the fixed-size `Vector512<T>` explicitly. The same source compiles to optimal width on each machine.

### Layer 3 — sized vectors `Vector128/256/512<T>`

When you need a specific width (e.g. because the algorithm is naturally 128-bit, or you want to fall back gracefully when AVX-512 isn't available):

```csharp
using System.Runtime.Intrinsics;

if (Vector256.IsHardwareAccelerated)
{
    Vector256<int> v = Vector256.Create(1, 2, 3, 4, 5, 6, 7, 8);
    Vector256<int> w = v * Vector256.Create(2);
}
```

### Layer 4 — hardware intrinsics

The bottom of the stack. Each method maps to one specific machine instruction:

```csharp
using System.Runtime.Intrinsics.X86;

if (Avx2.IsSupported)
{
    // Compute the absolute value of 8 ints in one instruction.
    // Avx2.Abs returns unsigned lanes, so the result type is Vector256<uint>.
    Vector256<uint> abs = Avx2.Abs(Vector256.Create(-1, -2, 3, -4, 5, -6, 7, -8));
}
```

Use these when you need an instruction that the portable APIs don't expose (CRC32, PCLMULQDQ, AES-NI, gather/scatter, ternary logic on AVX-512). Always guard with the `IsSupported` check — it's a JIT-time constant the runtime folds away.

## What you get and where you don't

A canonical sum of 1M floats:

| Implementation | Throughput |
|---|---|
| Naive scalar loop | ~1.0× |
| Multi-accumulator scalar loop | ~3–4× |
| `Vector<float>` portable | ~6–12× (NEON 4× width; AVX2 8× width) |
| AVX-512 sized intrinsics | ~16–20× |
| Memory-bandwidth bound (very large array) | capped by ~30–50 GB/s DRAM |

Two ceilings clamp this:

1. **Instruction throughput** — the ports the vector ops use. Often there's only one FP-ADD port per core; pipelining helps, but you can't go past the per-cycle throughput.
2. **Memory bandwidth** — beyond cache, you're limited by DRAM (~30 GB/s typical socket, ~200+ GB/s on big servers). A SIMD loop that pulls everything from DRAM tops out the same as a scalar loop that pulls everything from DRAM.

The right mental model: **SIMD speeds up loops that fit in cache; outside cache, layout and prefetching matter more than vector width**.

## Hot patterns where SIMD wins

- **Bulk numerical** — sums, dot products, FFTs, image convolutions.
- **Bytewise string scanning** — `IndexOf` on UTF-8 / ASCII (BCL does this internally).
- **Bit twiddling at scale** — `BitOperations.PopCount`, `Avx2.MoveMask`, etc.
- **JSON / column parsing** — `Vector256.Equals` to find delimiters across 32 bytes at a time.
- **Hashing** — CRC32, xxHash, the new `XxHash3` BCL type.

## Hot patterns where SIMD loses

- **Per-element control flow.** Lanes that go down different paths waste work.
- **Pointer-chasing.** SIMD can't help when each step depends on the previous result.
- **Small loops.** Setup + tail handling cost more than they save.

## Concurrency angle

SIMD itself doesn't change the concurrency story — each instruction is local to one core. But:

- A SIMD-heavy loop generates *more* memory bandwidth per cycle, which can make a workload memory-bandwidth-bound earlier than expected. Two SIMD threads on the same memory channel may starve each other.
- AVX-512 used to cause down-clocking on early Skylake-X parts (the whole core throttled when hot AVX-512 ran). On Sapphire Rapids and Granite Rapids this is almost gone, but check on your specific SKU.
- ARM SVE's "vector length agnostic" model means the *same binary* runs at 128 b on M3 and at 512 b on a hypothetical V3. Don't bake in `Vector<T>.Count` at startup and cache it — read it where you use it.

## Lab

```bash
dotnet run --project 00-Prerequisites -- 9
```

`SimdSpeedupDemo` sums a `float[]` two ways: a scalar loop and a `Vector<float>` loop. On AVX2 expect ~6–8× on cache-resident data; on Apple Silicon NEON expect ~3–4×.

## Further reading

- **Stephen Toub** — *Performance improvements in .NET <year>* posts on the .NET blog; the SIMD sections are the canonical reference for what the JIT just learned to vectorise.
- **`System.Runtime.Intrinsics` docs** on learn.microsoft.com — one page per ISA extension with every intrinsic listed.
- **Wojciech Muła** — `0x80.pl/notesen/`, dozens of articles on practical SIMD with measurements.
- **simdjson** project (the C++ original) — read the algorithms, port the ideas to .NET hardware intrinsics.
