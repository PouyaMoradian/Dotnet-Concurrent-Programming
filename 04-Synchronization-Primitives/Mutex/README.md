# Mutex

A **kernel** mutex — meaning even uncontended acquisition crosses the kernel boundary. Slow compared to `lock`. The reasons to use it anyway:

1. **Cross-process synchronisation.** A named `Mutex` is visible to every process on the machine that knows the name.
2. **Wait-handle interop.** It's a `WaitHandle`, so it composes with `WaitHandle.WaitAll`, `WaitAny`, `SignalAndWait`.
3. **Single-instance application detection.**

## Single-instance gate (canonical use)

```csharp
const string mutexName = @"Global\MyApp.SingleInstance.{a4f1...}";
using var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
if (!createdNew)
{
    Console.Error.WriteLine("Another instance is already running.");
    return 1;
}
// Run app...
mutex.ReleaseMutex();
```

`Global\` prefix on Windows means session-spanning visibility (across users). On Linux, named mutexes are filesystem-backed (`~/.config/.../`).

## The "abandoned mutex" trap

If a process holding a mutex crashes, the mutex enters the **abandoned** state. The next `WaitOne` succeeds but throws `AbandonedMutexException`. **Always handle this** — the protected state may be inconsistent.

```csharp
try { mutex.WaitOne(); }
catch (AbandonedMutexException)
{
    // We *did* acquire the mutex. Verify state, repair, log loudly.
}
```

## Costs

| Operation | Approx cost |
|---|---|
| Create / open named | hundreds of µs (kernel object allocation; security descriptor) |
| `WaitOne` uncontended | ~1 µs |
| `WaitOne` contended | µs to ms (futex/event roundtrip) |

Compare with `lock` at ~10 ns uncontended. So: **use `Mutex` only when you need a kernel object** (cross-process or wait-handle composition).

## Cross-process counter example

```csharp
// Two processes share a single counter via a memory-mapped file.
using var mmf = MemoryMappedFile.CreateOrOpen("Global\\MyApp.Counter", 8);
using var view = mmf.CreateViewAccessor();
using var mutex = new Mutex(false, "Global\\MyApp.Counter.Mutex");

mutex.WaitOne();
try
{
    view.Read<long>(0, out var x);
    x++;
    view.Write(0, x);
}
finally { mutex.ReleaseMutex(); }
```

For high-frequency cross-process synchronisation, named mutex + memory-mapped file is the lowest-latency option in pure managed .NET. For ultra-low-latency you'd reach for shared-memory + atomics via `Unsafe.As<long>(...)` and forgo the mutex altogether (lock-free) — see [05/Atomic-Operations](../../05-Atomic-Operations/).
