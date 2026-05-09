# Native interop and concurrency

When you P/Invoke into native code, two concurrency-relevant things happen:

1. **GC mode transition.** The thread switches from "GC-cooperative" (must reach a safepoint for the GC to suspend it) to "GC-preemptive" (the GC can ignore it). Marshalling code emits the transition.
2. **Thread blocking.** If the native call blocks (e.g., `WaitForSingleObject`), the thread is blocked from the OS' perspective. The thread pool's hill-climber may add a worker to compensate.

## Implications

- **Long native blocks starve the pool.** Same lesson as sync-over-async — don't block pool workers for arbitrary durations. For long native waits, use a dedicated thread.
- **GC suspension and native code.** A GC that needs to run while many threads are deep in native code waits for them to come back (or stops the world if it can). Quick P/Invoke calls don't impact GC noticeably; long ones do.
- **`SuppressGCTransition`** ([attribute](https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.suppressgctransitionattribute)) skips the transition for very fast native calls (e.g., `getpid`, `Environment.TickCount` shims). The thread stays GC-cooperative; the call must not block. Use only for proven hot paths.

## Modern P/Invoke: `[LibraryImport]` (source-generated)

```csharp
[LibraryImport("libc", SetLastError = true)]
internal static partial int sched_yield();

[LibraryImport("kernel32.dll")]
internal static partial uint GetCurrentThreadId();
```

Source-generated marshalling — faster, AOT-friendly, no runtime reflection. **Always prefer to `[DllImport]` for new code.**

## Threading-related native APIs

| You want | API |
|---|---|
| Pin to specific CPU on Linux | `pthread_setaffinity_np` (libc) |
| Pin on Windows | `SetThreadAffinityMask` (kernel32) |
| Get current thread id | `Environment.CurrentNativeThreadId` (.NET 8+) — no P/Invoke |
| Spinlock with hint to CPU | use `SpinWait`; `Thread.SpinWait` calls `pause`/`yield` |
| Yield CPU politely | `Thread.Yield()` / `SchedYield` |
| Set thread name (for debugger) | `Thread.CurrentThread.Name = "..."` (managed) — also surfaces to OS on .NET 5+ |

## Marshalling threads: COM apartments (Windows-only)

Apartment threading model affects COM calls. Marked with `[STAThread]` / `[MTAThread]` on `Main` or `Thread.SetApartmentState`. Modern .NET on Linux/macOS doesn't have apartments — they're a Windows COM concept. Relevant only for legacy Win32 / Office automation.

## Pinning managed memory for native calls

```csharp
fixed (byte* p = buffer)
{
    NativeCall(p, buffer.Length);
}
```

While `fixed`, the GC won't move the buffer. Don't hold pinned memory long — it fragments the heap.

For longer-lived pinning, use `GCHandle.Alloc(obj, GCHandleType.Pinned)` and free explicitly.

## Concurrency tip

Native callbacks that fire from a non-managed thread (e.g., a Unix signal handler, a kernel completion callback) need careful marshalling. The first call from such a thread invokes the runtime's "reverse P/Invoke" path. From a concurrency standpoint:

- Don't capture `AsyncLocal<T>` from a managed scope and expect it on a callback. The callback's `ExecutionContext` is fresh (or `null`).
- Don't take a `lock` you also take from managed code in the foreground — order matters; trivial deadlock potential.
- Push the work onto the thread pool with `ThreadPool.UnsafeQueueUserWorkItem` and let normal managed code handle it from there.
