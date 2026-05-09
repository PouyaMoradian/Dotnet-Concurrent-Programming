# Kernel vs User Mode

A CPU runs at one of two privilege levels: **user mode** (your code) and **kernel mode** (the OS). Crossing the boundary — a *system call* — is more expensive than most developers think. After Spectre/Meltdown, the cost has roughly doubled compared to pre-2018.

| Operation | Approx cost |
|---|---|
| User-mode function call | ~1 ns |
| Branch mispredict | ~5 ns |
| L3 cache miss | ~30 ns |
| `syscall` instruction (no real work) | ~100–300 ns post-mitigations |
| Lightweight syscall + a few µs of kernel work | ~1 µs |

## Why it's relevant to .NET concurrency primitives

Almost every .NET sync primitive has a *fast path* in user mode that escalates to a kernel wait only on contention.

| Primitive | User-mode fast path | Kernel-mode escalation |
|---|---|---|
| `lock` (Monitor) | Lightweight bit field on the object header; a CAS may succeed without entering the kernel | Wakeup-on-release uses a kernel event |
| `SemaphoreSlim` | Atomic decrement of count + spin | Kernel-mode `Semaphore` only when count exhausted |
| `ManualResetEventSlim` | Atomic flag + spin | Kernel `ManualResetEvent` if waiters too long |
| `SpinLock` | All-user-mode | None — `SpinLock` never sleeps |
| `Mutex` | None — kernel object always | Always kernel; cross-process |

## The Linux primitive: futex

Linux's *fast user-space mutex* — `futex(2)` — is the building block. It's:

- An *integer in user memory* protected by a pair of syscalls (`FUTEX_WAIT`, `FUTEX_WAKE`).
- Uncontended path: a CAS in user mode; no syscall.
- Contended path: a single syscall to park the thread.

CoreCLR's `Monitor` and `SemaphoreSlim` use futex on Linux for the kernel-mode escalation path.

## Implications for code

- **Don't measure `lock` cost with no contention** and conclude it's expensive — uncontended `lock` in modern .NET is ~10–20 ns.
- **Do measure with contention** — there a `lock` can take 1+ µs because of the futex/event roundtrip. This is where `SpinWait`/`SpinLock` win, *up to a point*.

## P/Invoke also crosses the boundary

P/Invoke from managed → native is itself a boundary cross. The marshaller emits a stub that:

1. Transitions the GC mode from "GC-cooperative" → "GC-preemptive" (so the GC can run while we're in native code).
2. Calls the native function.
3. Transitions back; possibly suspends if a GC is in progress.

The cost is ~10–30 ns per call, much less than a syscall, but it adds up in tight P/Invoke loops. .NET 7 introduced `[LibraryImport]` (source-generated marshalling), which is faster and AOT-friendlier than the older `[DllImport]`.

```csharp
[LibraryImport("user32.dll")]
internal static partial int MessageBoxA(IntPtr hWnd, [MarshalAs(UnmanagedType.LPStr)] string text, [MarshalAs(UnmanagedType.LPStr)] string caption, uint type);
```

## Anti-pattern: spamming syscalls in a hot loop

`Stopwatch.GetTimestamp` is a user-mode `rdtsc` on x86 / a `cntvct_el0` on ARM — cheap. `DateTime.UtcNow` is more expensive (formats and TZ-aware). `File.WriteAllText` for a "log" line in a hot path is *catastrophic*. Buffer everything; flush rarely.
