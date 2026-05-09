# `Interlocked` — atomic operations on a single location

`System.Threading.Interlocked` is the entry point for atomic CPU operations from managed code. Each method maps to a hardware instruction (`lock cmpxchg`, `lock xadd`, `lock xchg` on x86; `LDXR/STXR` loops on ARM64).

## API map

| Method | Effect | x86 instruction |
|---|---|---|
| `Increment(ref x)` | `++x` atomically; returns new value | `lock xadd` |
| `Decrement(ref x)` | `--x` atomically; returns new value | `lock xadd` |
| `Add(ref x, n)` | `x += n`; returns new value | `lock xadd` |
| `Exchange(ref x, v)` | Set x to v; return old value | `lock xchg` |
| `CompareExchange(ref x, new, expected)` | If x==expected, set x=new; return old | `lock cmpxchg` |
| `Read(ref long x)` | Atomic 64-bit read on 32-bit | `lock cmpxchg` (no-op compare) |
| `Or` / `And` / `Xor` (.NET 5+) | Bitwise atomic | `lock or` / `and` / `xor` |
| `MemoryBarrier()` | Full fence | `mfence` (x86) / `dmb ish` (ARM) |

All take `ref T` for `T ∈ { int, long, IntPtr, object, T (reference) }`. .NET 7+ added `uint`/`ulong` overloads.

## Cost

Uncontended: ~10–20 ns. Effectively the same as a plain `lock` (which itself is a CAS). The big difference shows up under *contention*: an `Interlocked` operation is one CAS round-trip; a contended `lock` is a CAS + maybe a kernel wait. So:

- **Single hot counter, low/medium contention:** `Interlocked` wins.
- **Single hot counter, high contention:** both lose; you need sharding.
- **Multi-step critical section:** `lock` is the right tool.

## Tricks worth knowing

### Atomic max

```csharp
void Max(ref long target, long candidate)
{
    long old;
    do
    {
        old = Volatile.Read(ref target);
        if (candidate <= old) return;
    } while (Interlocked.CompareExchange(ref target, candidate, old) != old);
}
```

### Atomic add-if-greater (saturating arithmetic)

```csharp
bool TryAdd(ref long target, long delta, long max)
{
    long old, next;
    do
    {
        old = Volatile.Read(ref target);
        next = old + delta;
        if (next > max) return false;
    } while (Interlocked.CompareExchange(ref target, next, old) != old);
    return true;
}
```

### Reference-typed atomic publish

```csharp
public Configuration Current => Volatile.Read(ref _current);
public void Publish(Configuration v) => Volatile.Write(ref _current, v);
public bool TryPublish(Configuration v, Configuration expected) =>
    Interlocked.CompareExchange(ref _current, v, expected) == expected;
```

The reference write is atomic because reference fields are pointer-sized and aligned.

## Memory ordering

On x86, `Interlocked` operations are full fences. On ARM64, the JIT inserts the appropriate `dmb` barrier so the *.NET memory model* gives you sequential consistency around an `Interlocked` op. You don't need to think about ISA differences as long as you only use `Interlocked` and `Volatile`.

## Common pitfalls

1. **Atomic ≠ correct.** Two consecutive `Interlocked.Increment`s are *not* a single atomic operation. Two different fields cannot be updated atomically without a lock or a packed struct.
2. **`long` on 32-bit.** Plain `long` reads/writes are *not* atomic on 32-bit; use `Interlocked.Read` or `Volatile.Read` (.NET 7+ has 64-bit overload). Practically irrelevant on 64-bit-only deployments.
3. **`ref` to a stack variable.** Atomic on a local makes no sense — there's no contention. The compiler usually catches this.
4. **`Interlocked.Increment` on a pinned struct field via `ref`.** Be careful that the struct isn't copied; you'd be incrementing the copy. Use `ref` parameters explicitly.
