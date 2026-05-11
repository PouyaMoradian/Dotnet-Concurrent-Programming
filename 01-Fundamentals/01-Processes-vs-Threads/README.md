# Processes vs Threads — overview

Almost every confusion in this area comes from treating "process" and "thread" as synonyms for "a thing that does work". They're not. A process is a **container of resources**; a thread is a **scheduling unit** inside that container. You can have one without the other (a process with zero threads is a corpse waiting to be reaped; a thread without a process is impossible).

## The 30-second summary

| | Process | Thread |
|---|---|---|
| Address space | Own (isolated by MMU) | Shared with siblings in the same process |
| Crash blast radius | Confined to itself | Whole process dies |
| File handles, sockets | Own table | Shared with siblings |
| Default stack size | (process has heap) | 1 MB user-mode (Windows / Linux x64), 4 MB on Windows Server |
| Creation cost | ms range — `fork`/`CreateProcess` | µs range — `pthread_create`/`CreateThread` |
| Communication | IPC (pipes, sockets, shared memory, gRPC) | Shared memory + sync primitives |
| Scheduling unit | No (it's a container; threads are scheduled) | Yes |
| Crashes affect | Itself | All sibling threads |

## When to choose each

- **Threads** when you need shared memory, low coordination latency, and trust the code to not corrupt invariants. Most app concurrency.
- **Processes** when you need *isolation* — untrusted plugins, browser tabs, the JIT compiler service in some IDEs, multi-tenant work that must not leak.

Modern .NET code rarely *chooses* between them at the call site. You use threads (implicitly, via the ThreadPool) inside your process; you reach for separate processes when the threat model demands isolation (sandboxing, fault containment, language interop).

## Read deeper

| File | What it covers |
|---|---|
| [01-Address-Spaces.md](01-Address-Spaces.md) | What "address space" really means — virtual memory, the MMU, page tables, why threads share but processes don't |
| [02-Threads-In-Depth.md](02-Threads-In-Depth.md) | The kernel's view of a thread — TCB, scheduler classes, time slices, context switches, what a context switch actually costs |
| [03-DotNet-Threads.md](03-DotNet-Threads.md) | The managed `Thread` type, `ExecutionContext`/`SynchronizationContext`, GC interaction, when to use raw `Thread` |
| [04-Cost-Comparison.md](04-Cost-Comparison.md) | The measured cost of starting a thread, queuing a task, and launching a process — on real hardware, with the demo |

## Demo

`ProcessVsThreadDemo` quantifies thread vs Task vs async-no-op on your machine. Approximate numbers on a 2024-vintage laptop:

| Action × 1000 | Wall time |
|---|---|
| `new Thread` start + join | ~150–400 ms |
| `Task.Run` (pool) | ~1–3 ms |
| Async no-op | ~0.1 ms |

A two-orders-of-magnitude gap between threads and tasks; another order of magnitude down to async no-ops. **The thread pool is not an optimisation; it's the right answer** for fine-grained units of work.
