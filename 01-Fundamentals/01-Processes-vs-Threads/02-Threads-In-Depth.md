# Threads in depth — the kernel's view

A thread is the OS scheduler's atomic unit of CPU work. The CPU runs *one* thread per core at any instant; the scheduler decides which one. Everything else (processes, fibers, tasks, goroutines, …) is layered on top of that one primitive.

## What the kernel actually stores about a thread

Per-thread, in kernel memory, the OS keeps a **Thread Control Block (TCB)**:

- Saved register state (general-purpose registers, FP/SIMD registers, the instruction pointer).
- Kernel stack pointer (each thread also has a tiny *kernel-mode* stack used while system calls execute).
- User stack base + size + guard page address.
- Scheduling fields: priority, affinity mask, last-run timestamp, accumulated CPU time.
- Thread Local Storage (TLS) pointer.
- The process (parent) it belongs to — for accounting.
- Wait state: if the thread is blocked, what is it waiting on?

The TCB is what gets reloaded on a **context switch**. A context switch is a small, exact, but not free operation:

1. The current thread's registers get saved into its TCB.
2. The scheduler picks the next thread to run.
3. If it's in a different *process*, the MMU's page-table base register (`CR3` on x86-64) gets reloaded, which forces a TLB flush on most CPUs.
4. The new thread's registers (including its instruction pointer) get loaded.
5. Execution resumes from where the new thread left off.

On modern x86-64 Linux, a same-process context switch is ~1–3 µs of pure CPU. A cross-process switch is closer to ~3–10 µs because of the TLB cost. Multiply by hundreds of thousands per second on a busy server and you have a meaningful overhead — which is the reason the ThreadPool aggressively reuses threads rather than spinning up a new OS thread per work item.

## How the scheduler decides

Both Linux and Windows use **priority-based, preemptive, multilevel feedback scheduling** (CFS on modern Linux, the multilevel priority scheduler on Windows). The simplified rules:

- Each thread has a priority. Higher priority preempts lower.
- Within the same priority, threads get **time slices** — typically ~10 ms — after which they're rotated to the back of the runnable queue.
- A thread that blocks (e.g., on IO) gets removed from the run queue. When the IO completes the thread is added back, often with a temporary priority boost so latency-sensitive tasks (UI) feel responsive.
- A thread can be **bound to a CPU** (affinity); otherwise the scheduler can migrate it to balance load, at the cost of cache locality.

In .NET specifically you almost never set thread priority. The exceptions are real-time audio, hard real-time control loops, and benchmark harnesses. Setting a thread to `ThreadPriority.Highest` to "make it faster" is a classic anti-pattern: if it does CPU-bound work it just starves the rest of your app; if it does IO it gets no benefit because it was sleeping anyway.

## Stack sizes — the underrated cost of `new Thread`

A `new Thread()` allocates a default stack of **1 MB** on x64 Windows and Linux user-mode (4 MB on Windows Server SKUs). That allocation is *reserved* (address space committed but not necessarily backed by physical memory) until the thread first touches it. Still, 1 MB × 10,000 threads = 10 GB of address space. That's why "spin up a thread per request" hit a wall in the late '90s and why the C10K problem birthed event loops, then async/await.

You can override the stack with `new Thread(work, 256 * 1024)` (256 KB). It works, but if any frame on that thread overflows you get a `StackOverflowException` that crashes the process unrecoverably. Don't do this unless you've measured and have a reason.

The ThreadPool's workers use the same default, but you only pay for the threads it actually starts — typically `Environment.ProcessorCount`-ish in steady state, expanding under starvation.

## Threads vs lightweight alternatives

Several runtimes solved "threads are too expensive for fine-grained units" without leaving the thread abstraction:

- **Fibers / coroutines** — cooperatively scheduled user-mode threads. Windows has `CreateFiber`; .NET briefly experimented and abandoned it (the GC has trouble with non-OS threads). Tasks fill that role today.
- **Green threads** — fully user-scheduled. Go's goroutines, Erlang's processes, Java's virtual threads (Project Loom). They make blocking IO cheap because "blocking" is a user-mode park, not a kernel one. .NET doesn't have them; it picked async/await as the equivalent solution.
- **Tasks** — what .NET went with. A `Task` is a continuation-passing description of work; the ThreadPool *runs* tasks on a small fixed pool of OS threads. No new abstraction below the OS thread; what's new is the scheduling layer above it.

If you've used goroutines and you're learning .NET, the mental mapping is "goroutine ≈ Task; channel ≈ Channel; runtime scheduler ≈ ThreadPool". The semantics aren't identical (a goroutine survives a blocking syscall by parking itself, a Task does not), but the spirit is.

## What a thread context switch costs you — the hidden bill

Beyond the ~1–3 µs of CPU, a context switch also:

- **Cools the L1/L2 caches** for the new thread (and warms them for the *old* thread, just in time for it to be evicted next time around). A cache miss costs ~100–300 cycles each.
- **Discards branch predictor state.**
- **On a cross-process switch, flushes the TLB.** Subsequent memory accesses pay page-walk cost (a few hundred cycles each) until the TLB warms back up.

So a context switch's *direct* cost is a couple of microseconds, but its *indirect* cost — the time the next thread spends running slowly because its cache is cold — can be 10–100× larger. That's the real reason work-stealing prefers LIFO-local pops: the task you just queued is hot in your cache, so popping it next preserves locality.
