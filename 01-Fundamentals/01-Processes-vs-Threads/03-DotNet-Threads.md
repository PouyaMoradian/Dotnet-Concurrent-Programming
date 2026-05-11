# Threads in .NET specifically

A managed `System.Threading.Thread` is a thin wrapper around an OS thread *plus* a stack of managed bookkeeping the CLR needs to make .NET work — garbage collection, exceptions, security context, culture, synchronisation context. Understanding that bookkeeping is what separates "I can call `new Thread`" from "I know why I shouldn't."

## What a managed thread carries that an OS thread doesn't

| Bookkeeping | What it does | Default behaviour |
|---|---|---|
| **Managed thread ID** | `Environment.CurrentManagedThreadId` — stable for the lifetime of the managed thread, even if the OS thread is recycled | Auto-assigned, small positive int |
| **Thread name** | Diagnostic only, shows up in dumps and the debugger | `null` until set |
| **Background flag** | Background threads don't keep the process alive | `Thread.IsBackground = false` for `new Thread`, **`true`** for ThreadPool threads |
| **Priority** | Hint to the OS scheduler | `ThreadPriority.Normal` |
| **Apartment state** | COM threading model (STA/MTA) — Windows only, basically only used by WPF/WinForms | `MTA` for ThreadPool, `STA` for UI |
| **Culture and UI culture** | `CultureInfo.CurrentCulture` and its UI sibling — affect formatting, parsing, resource lookup | Inherited |
| **`ExecutionContext`** | Captures `AsyncLocal<T>`, security context, logical call context | Flows across `await` and `Task.Run` by default |
| **`SynchronizationContext`** | "Where do I post the continuation back to?" — non-null on UI threads | `null` on ThreadPool and console apps |
| **TLS slots** | Storage for `[ThreadStatic]` fields and `ThreadLocal<T>` | Allocated lazily |

The flag people get bitten by most is `IsBackground`. A foreground `new Thread` keeps the process alive *even if `Main` has returned*. A console app that spins up a worker on `new Thread` and forgets to set `IsBackground = true` will not exit until the worker does. ThreadPool threads are always background; you don't have to think about this for `Task.Run`.

## ExecutionContext — the invisible carrier

Every time you `await`, `Task.Run`, or queue a callback to the ThreadPool, the runtime captures the current `ExecutionContext` and restores it on the other side. That's how `AsyncLocal<T>` works:

```csharp
static readonly AsyncLocal<string> CorrelationId = new();

await using var _ = Operation.Start();
CorrelationId.Value = "req-42";

await Task.Run(() =>
{
    // Different thread. But CorrelationId.Value is still "req-42" because
    // ExecutionContext flowed across the Task.Run boundary.
    Log.Info($"working on {CorrelationId.Value}");
});
```

This flow has two important properties:

- It's **copy-on-write**. Assigning to `CorrelationId.Value` inside the captured context creates a new context for the current logical thread of control; the parent's value is unchanged.
- It's **opt-out, not opt-in**. `ExecutionContext.SuppressFlow()` exists if you specifically need a callback that doesn't carry context (rarely — usually for runtime infrastructure).

You'll see deeper detail in [08-Async-Await-Deep-Dive](../../08-Async-Await-Deep-Dive/) and how ASP.NET Core abuses this to flow request scopes.

## SynchronizationContext — the resumption policy

A `SynchronizationContext` answers one question: "after this `await`, on which thread should the continuation run?" The runtime captures it when the `await` *begins* and posts the continuation back to it on completion (unless you opt out with `ConfigureAwait(false)`).

| Context | Posts to | Implication |
|---|---|---|
| `null` (console, ASP.NET Core) | The completing thread / pool | Continuations can hop threads freely |
| WPF `DispatcherSynchronizationContext` | The UI dispatcher | Continuations marshal back to the UI thread |
| WinForms | `Control.Invoke` queue | Same |
| Unity main-thread context | Unity's main thread | Same |
| ASP.NET *classic* (pre-Core) | The request thread | The reason `.Result` deadlocks classic ASP.NET |

In modern .NET (Core+), most server code runs with no synchronisation context. That's why "you don't need `ConfigureAwait(false)` in ASP.NET Core" is a fact, not an opinion — there's no context to capture. You still need it in library code that *might* run under a UI context.

## GC interaction — why you can't have user-mode threads in the CLR

The GC needs to **pause every managed thread** at a safepoint to compute the live set. "Safepoint" means the JIT has emitted enough metadata at that exact instruction for the GC to know which registers hold object references and which hold ordinary integers. JIT-compiled .NET code inserts safepoints at method calls, loop backedges, and a few other places.

This is why:

- A thread spinning in a tight loop with no safepoints can stall a GC. The runtime mitigates this with **hijacking** (modifying the return address) and **interrupted IO**.
- Native code called via P/Invoke is **outside** managed coordination. The runtime marks the thread as "in unmanaged code" and the GC proceeds without waiting for it; if the GC moves objects the thread holds, it pinned them first.
- Fibers and "real" green threads don't fit because the GC walks OS-thread stacks. .NET tried fibers in the early 2000s and abandoned them. Project Loom (Java) only works because the JVM redesigned this layer.

The upshot: in .NET, *the OS thread is the unit of GC observation*. Everything async-y is layered on top of it, not below it.

## When to use raw `Thread` vs `Task` / `Task.Run`

`Thread` is rarely the right primitive in modern code. Reach for `Task`/`Task.Run`/`Parallel`. Use `new Thread` only when you genuinely need:

1. **A thread that lives outside the pool.** E.g., a high-priority dedicated audio loop, or a long-running listener that you never want to share with other pool work. Use `Task.Factory.StartNew(work, TaskCreationOptions.LongRunning)` first — that gets you a dedicated thread *while still returning a Task*.
2. **Apartment state.** Legacy COM interop on Windows that requires STA. `Thread.SetApartmentState(ApartmentState.STA)` only works *before* the thread is started.
3. **Custom stack size.** A worker that needs a smaller stack (for memory) or a larger one (for deep recursion).
4. **Avoiding pool injection pressure.** A long-running thread that you definitely don't want the ThreadPool's hill-climbing heuristic to react to.

A useful heuristic: if your work item is shorter than ~100 ms, never use `new Thread` — the startup cost dominates. If it's many seconds and dedicated, consider `Task.Factory.StartNew(..., LongRunning)` instead of either.

## `Task` is not a thread

This is the most common conceptual error new .NET developers make:

> "I created 1000 Tasks, that means I have 1000 threads, right?"

No. A `Task` is a *promise of a future value*. It schedules work onto the ThreadPool, which has perhaps 16 threads on your machine. 1000 tasks share those 16 threads, picked up as workers become free. If those tasks are async and spend most of their time at `await`, they don't even occupy a worker while suspended — the worker goes off and runs another task in the meantime.

That's why the next chapter ([03-Synchronization-vs-Asynchrony](../03-Synchronization-vs-Asynchrony/)) emphasises that `async` is about **freeing the worker**, not about creating more of them.
