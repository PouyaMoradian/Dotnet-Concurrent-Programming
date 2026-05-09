# Memory visibility

A read by thread B of a value written by thread A is **not** guaranteed to see A's write — unless ordering primitives connect the two events. This is true on every modern CPU and every modern runtime, including .NET on x86 (which has a strong but not infinitely strong model).

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

## What fixes it

Three escalating tools, each from this repo's later chapters:

1. **`Volatile.Read` / `Volatile.Write`** — release on store, acquire on load. Prevents reorderings across the call.
2. **`Interlocked.*`** — atomic *and* a full memory barrier on most ops. Costs more.
3. **`Lock` / `Monitor`** — a critical section is also a barrier; entering it acquires, leaving it releases.

```csharp
// Fixed version using Volatile.
class Fixed
{
    static int data;
    static bool ready;

    static void Producer()
    {
        Volatile.Write(ref data, 42);    // release
        Volatile.Write(ref ready, true); // release — orders after the data write
    }

    static void Consumer()
    {
        while (!Volatile.Read(ref ready)) { /* spin */ }   // acquire — pairs with the writes
        Console.WriteLine(Volatile.Read(ref data));         // 42, guaranteed
    }
}
```

## The ECMA / CLR rules in two lines

- **Reference and primitive (≤ pointer-sized) writes are atomic** (no torn reads). 64-bit fields on 32-bit are *not*. Prefer `Interlocked.Read` for `long` portability.
- **The memory model is a relaxed model with release-store / acquire-load via `Volatile`.** It is *stricter* than C++'s `memory_order_relaxed` but weaker than sequential consistency. See [12-Memory-Model](../../12-Memory-Model-and-LowLevel/).

## Practical advice

If you find yourself reading or writing a shared variable without a lock:

1. Have you proved the read order doesn't matter? (E.g., a monotonically growing counter you read only for telemetry.)
2. If order matters: **always** use `Volatile.Read`/`Volatile.Write` or `Interlocked`.
3. Don't trust `volatile` (the C# keyword) on `long`/`double` — it doesn't fix tearing on 32-bit. `Interlocked.Read`/`Volatile.Read` (.NET 7+ overloads) does.

## Demo

The chapter's `MemoryVisibilityDemo` shows a working version using `Volatile`. Try removing the volatiles and running on ARM64 — you'll catch the rare miss.
