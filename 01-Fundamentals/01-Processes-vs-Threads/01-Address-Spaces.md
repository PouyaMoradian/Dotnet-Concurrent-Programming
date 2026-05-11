# Address spaces — what processes actually own

When the OS creates a process, the most important thing it gives it is a **virtual address space**: an apparently-private, contiguous range of bytes from `0x0000...0000` up to the architecture's user-mode limit (`0x00007FFF_FFFFFFFF` on x86-64 Windows, similar on Linux). Every pointer your code dereferences is a virtual address. The CPU's **Memory Management Unit (MMU)** translates that virtual address to a physical one using **page tables** that the kernel maintains, one set per process.

Two consequences fall straight out of this.

## Consequence 1 — isolation is free at the instruction level

When the OS switches from process `A` to process `B`, it points the MMU at `B`'s page tables (on x86-64, by reloading `CR3`). All addresses `B` issues now mean different physical pages than the same numeric addresses would have meant in `A`. There is no software check; the hardware *can't* see `A`'s memory while `B` is running. That's how a buggy process can't corrupt another, and why a process crash is contained.

```
Process A view:                       Process B view:
  0x7fff_aaaa  →  phys 0xc100         0x7fff_aaaa  →  phys 0xd200   (different page!)
  0x4000_0000  →  phys 0x8000         0x4000_0000  →  not mapped
                                                          → SIGSEGV / AV on access
```

That isolation is exactly what threads do **not** get. Threads inside the same process share the same page tables and therefore the same view of memory.

## Consequence 2 — sharing requires explicit mechanism

To share data *between processes* you have to ask the kernel for it:

- **Shared memory segments** (`shm_open` on POSIX, `CreateFileMapping` on Windows) — the same physical pages are mapped into both processes' address spaces, often at *different* virtual addresses.
- **Pipes, sockets, gRPC, MMF** — copy data through the kernel.
- **Memory-mapped files** — a file's bytes appear as part of the address space; multiple processes mapping the same file see the same physical pages.

Between threads, by contrast, *every byte of the heap is shared by default*. The pointer you take in thread A means the same physical memory in thread B. This is the source of both the power and the danger of threads.

## The full picture of a process's address space

Roughly (and modulo ASLR, which scrambles the bases):

```
high   ┌───────────────────────────────────────┐
       │  kernel-reserved (not user-accessible) │
       ├───────────────────────────────────────┤
       │  thread 3 stack (1 MB, grows down) ↓  │
       │  ──── guard page ────                  │
       │  thread 2 stack (1 MB, grows down) ↓  │
       │  ──── guard page ────                  │
       │  thread 1 stack (1 MB, grows down) ↓  │
       │  ──── guard page ────                  │
       │  shared libraries (libc, coreclr, …)   │
       │  memory-mapped files                   │
       │                                       │
       │  heap (GC heap + native, grows up) ↑  │
       │                                       │
       │  BSS / static data                    │
       │  initialised globals                  │
       │  code (read-only, executable)         │
low    └───────────────────────────────────────┘
```

The big takeaway: **each thread gets its own stack region in the same address space**. That's why a thread can take a pointer to a local variable and pass it to another thread — they share the heap *and* can read each other's stacks if given a pointer. That's also why a stack overflow on one thread doesn't crash a sibling: the guard page below each stack catches the overrun and the OS sends a `StackOverflowException` to that thread only. (In .NET, that exception is unrecoverable and tears the process down — but that's a CLR policy choice, not an OS necessity.)

## What this means for .NET

- The .NET garbage collector traces references **across all threads** because the heap is shared by all of them. A thread's stack is a GC root: the GC pauses your threads at safepoints and walks their stacks to find live references.
- A `static` field is shared by all threads in the process. Writing to it without synchronisation is a data race.
- A field on a heap-allocated object reached from two threads is shared regardless of who allocated it.
- A local variable is **not** automatically thread-local — it's just thread-private *by virtue of nobody having a pointer to it*. Capture it in a lambda passed to `Task.Run` and you've shared a closure field on the heap.

```csharp
int counter = 0;
await Task.WhenAll(
    Task.Run(() => { for (int i = 0; i < 1000; i++) counter++; }),
    Task.Run(() => { for (int i = 0; i < 1000; i++) counter++; }));
// counter is captured by reference into a compiler-generated display class
// living on the heap. Both tasks share it. Result is a race.
```

## Why processes are still useful in 2026

Three reasons keep multi-process designs alive even when threads are free:

1. **Security boundary.** A browser tab is a separate process so a compromised renderer can't read your bank session.
2. **Fault isolation.** A native plugin that segfaults shouldn't kill the host. Out-of-process plugin hosting (the way some IDEs run language servers, the way Office hosts add-ins) is exactly this.
3. **Per-process resource accounting.** The OS only meters CPU, memory and file handles per *process*. If you need to ulimit / cgroup something independently, it must be a separate process.

None of these are "performance" reasons. Performance is on threads' side. The choice is about trust and blast radius.
