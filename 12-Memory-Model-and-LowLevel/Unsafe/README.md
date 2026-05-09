# `System.Runtime.CompilerServices.Unsafe`

A static class that lets you do pointer-like things without `unsafe` blocks. Useful for high-perf primitives; dangerous everywhere else.

## What's in it

| Method | Purpose |
|---|---|
| `Unsafe.As<T>(ref source)` / `Unsafe.As<TFrom, TTo>(ref source)` | Reinterpret-cast |
| `Unsafe.AsRef<T>(in source)` | Get a writable ref from a readonly ref |
| `Unsafe.SizeOf<T>()` | sizeof at runtime |
| `Unsafe.NullRef<T>()` | A "null" reference (use with care) |
| `Unsafe.IsNullRef<T>(ref T)` | Test |
| `Unsafe.AddByteOffset<T>(ref T, IntPtr)` | Pointer arithmetic |
| `Unsafe.ReadUnaligned<T>(ref byte)` | Read T from any byte offset |
| `Unsafe.WriteUnaligned<T>(ref byte, T)` | Write T to any byte offset |

## Typical use cases

### Reinterpret without copying

```csharp
// Treat a byte span as a span of int (must be size-aligned and length-divisible)
var bytes = new byte[256];
ref var asInts = ref Unsafe.As<byte, int>(ref bytes[0]);
asInts = 42;
```

### Read primitives from arbitrary buffers

```csharp
ushort be = BinaryPrimitives.ReadUInt16BigEndian(buffer);            // preferred
ushort raw = Unsafe.ReadUnaligned<ushort>(ref buffer[offset]);       // skip endian — your problem
```

### Hot-path skip checks

```csharp
public static int Compare<T>(T a, T b) where T : IComparable<T>
    => Unsafe.IsNullRef(ref Unsafe.AsRef(in a)) ? -1 : a.CompareTo(b);
```

## Dangers

1. **No type safety.** `Unsafe.As<int, double>` will reinterpret bytes; the runtime won't catch you.
2. **No bounds checking.** `AddByteOffset` past the end of an array gives you arbitrary memory access.
3. **GC.** A `ref` to an object's interior must respect GC rules. Pin with `fixed` or `MemoryMarshal.GetReference` carefully.
4. **AOT/IL trimming.** Some `Unsafe` usages prevent the linker from removing types. Trim-warning territory.

## Better alternatives in most cases

- `Span<T>` / `Memory<T>` for slices.
- `BinaryPrimitives` for endian-correct primitives.
- `MemoryMarshal` for casts between span types.
- Source generators for hot paths instead of hand-written `Unsafe`.

If you can solve the problem without `Unsafe`, do.

## When Unsafe is worth it

- Custom serialisers (Span-of-bytes ↔ struct), where every cycle matters.
- Hash functions, compression, encryption inner loops.
- High-frequency network parsers.
- Implementations of low-level concurrency primitives.

For the rest of us, the "modern" path is `Span<T>` + intrinsics where helpful. `Unsafe` is the escape hatch for the cases the abstractions don't cover.
