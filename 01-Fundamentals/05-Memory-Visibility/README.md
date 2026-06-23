# Memory visibility — overview

A read by thread B of a value written by thread A is **not** guaranteed to see A's write — unless ordering primitives connect the two events. This is true on every modern CPU and every modern runtime, including .NET on x86 (which has a strong but not infinitely strong model).

This is the single most surprising fact about concurrent programs. Even on a single CPU, the JIT can keep values in registers across a method that has no synchronisation; even on the strongest hardware, two writes can become visible to another core in the *opposite* order from the source code. If you've ever had a bug that "shouldn't happen" disappear when you added a `Console.WriteLine` for debugging, this is the layer where it lives.

## The minimal counterexample

```csharp
class Bug
{
    static int data;
    static bool ready;

    static void Producer()
    {
        data = 42;
        ready = true;
    }

    static void Consumer()
    {
        while (!ready) { /* spin */ }
        Console.WriteLine(data);   // can print 0 in theory; very rare on x86, easy on ARM
    }
}
```

On ARM64 (Apple Silicon, AWS Graviton, Ampere), this can and does print 0. On x86 it almost never does — but "almost never" is not "never", and the JIT is allowed to reorder the two writes anyway.

The bug isn't "the consumer runs before the producer". The producer's `data = 42` is written *first*. The bug is that the consumer may see the writes happen in a different order than the producer made them — because the CPU's store buffer or the JIT's optimiser is free to reorder writes that have no ordering constraint between them.

## Read deeper

| File | What it covers |
|---|---|
| [01-Why-Reads-Lie.md](01-Why-Reads-Lie.md) | CPU caches, store buffers, compiler reordering — the physical reason this happens |
| [02-Memory-Models.md](02-Memory-Models.md) | x86 TSO vs ARM weak model, the CLR memory model, ECMA-335 rules |
| [03-DotNet-Tools.md](03-DotNet-Tools.md) | `volatile`, `Volatile.Read/Write`, `Interlocked`, `lock`, full barriers — what each one buys |

## Practical advice

If you find yourself reading or writing a shared variable without a lock:

1. Have you proved the read order doesn't matter? (E.g., a monotonically growing counter you read only for telemetry.)
2. If order matters: **always** use `Volatile.Read`/`Volatile.Write` or `Interlocked`.
3. Don't trust `volatile` (the C# keyword) on `long`/`double` — it doesn't fix tearing on 32-bit. `Interlocked.Read` (for `long`/`ulong`, available since .NET Framework 2.0) or `Volatile.Read` does.

## Demos

- `MemoryVisibilityDemo` shows a working version using `Volatile`. Try removing the volatiles and running on ARM64 — you'll catch the rare miss.
- `TornLongReadDemo` shows the worse failure mode: a 64-bit field, read concurrently with writes, can produce a value that *was never written*. On 32-bit hardware it's routine; on 64-bit with deliberate misalignment, observable.
