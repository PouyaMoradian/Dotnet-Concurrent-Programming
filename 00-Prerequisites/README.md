# 00 — Prerequisites: how the hardware behaves

> **Layer:** CPU & memory
> **Reading time:** ~30 minutes
> **You'll be ready to:** read every later chapter without hand-waving "the CPU does this"

If you skip this chapter, every conversation about lock-free code, false sharing, the CLR memory model, or `volatile` becomes a faith exercise. Concurrency *is* a hardware story before it is a language story. The C# you write does not run on an idealised von Neumann machine — it runs on a multi-socket, multi-core, multi-cache, out-of-order, store-buffered, prefetching, branch-predicting beast that will reorder your reads and writes and only pretend to obey the program order *you* wrote.

## What's in this chapter

| Section | What it teaches |
|---|---|
| [CPU-Architecture](CPU-Architecture/) | Pipelines, ILP, out-of-order execution, store buffers |
| [Context-Switching](Context-Switching/) | What the OS does when it preempts a thread, and why it costs ~1–10 µs |
| [Cache-Coherency](Cache-Coherency/) | MESI states, the `RFO` (Read-For-Ownership) message, and why writers are expensive |
| [NUMA](NUMA/) | Why a thread on socket 0 reading memory bound to socket 1 can be 2× slower |
| [False-Sharing](False-Sharing/) | Two threads writing two unrelated fields can still serialize through the cache |

Each subfolder has its own README with code that demonstrates the effect on the metal.

---

## The four hardware facts every .NET dev needs

### 1. A "core" is not a "thread"

A modern CPU exposes **logical processors** to the OS. With Intel Hyper-Threading or AMD SMT, two logical processors share one physical core's execution units. `Environment.ProcessorCount` returns logical processors. Two threads pinned to the same physical core compete for the ALU, the L1, and the load/store buffers — `ProcessorCount` overstates your real parallel headroom by ~2× when you're CPU-bound.

```csharp
Console.WriteLine($"Logical processors: {Environment.ProcessorCount}");
// On Linux:    cat /proc/cpuinfo | grep -c ^processor
// On Windows:  Get-CimInstance Win32_Processor | Select NumberOfCores, NumberOfLogicalProcessors
```

### 2. Memory is a hierarchy, and you usually live in L1

Approximate latencies on a modern x86 server CPU:

| Level | Capacity (per core) | Latency | Notes |
|---|---|---|---|
| Register | ~32 × 64-bit | 0 cycles | The JIT's allocator decides |
| L1d (data) | 32–48 KB | ~4 cycles | Per physical core |
| L1i (instruction) | 32 KB | ~4 cycles | Per physical core |
| L2 | 512 KB – 2 MB | ~12 cycles | Per physical core |
| L3 (LLC) | 8 – 256 MB | ~40 cycles | Shared across cores in a socket |
| Local DRAM | GBs | ~80–100 ns (~250 cycles) | NUMA-local |
| Remote DRAM | GBs | ~150–200 ns | Crossing socket = QPI/UPI hop |

A CPU at 3 GHz executes ~3 instructions per ns when hot in L1. A miss to DRAM is **~250–600 wasted cycles** — enough to do hundreds of arithmetic ops. *Cache locality is throughput.*

### 3. Cache is coherent, but coherence has a cost

Modern x86 maintains **cache coherence** across cores using protocols like MESI / MESIF / MOESI. When core A writes to a line that core B has cached, A must first invalidate B's copy (an `RFO` — Read For Ownership message on the ring/mesh interconnect). The line ping-pongs between caches. This is why:

- Two threads incrementing **the same** `int` (with `Interlocked.Increment`) serialize through the cache, and the throughput collapses with core count.
- Two threads writing **different** `int`s that happen to live on the same 64-byte cache line *also* serialize. That's [false sharing](False-Sharing/) and it's stunningly easy to write by accident.

### 4. The CPU and the JIT both reorder your code

What you wrote:
```csharp
_data = ComputeExpensive();
_ready = true;
```

What can be observed by another core: `_ready == true` while `_data == null`. The reasons are layered:

- **Compiler / JIT** may reorder unrelated stores.
- **CPU store buffer** lets a core "retire" a store before it becomes globally visible.
- **CPU memory model** (x86 is TSO — total-store-order — which forbids most reorderings; ARM64 is much weaker).

The CLR's own memory model gives you a few rock-solid guarantees, plus `Interlocked`, `Volatile`, and `MemoryBarrier` to insert ordering. We unpack this in [12-Memory-Model-and-LowLevel](../12-Memory-Model-and-LowLevel/). For now: **don't** assume program order.

---

## Run the labs

Each subfolder has a small `Program.cs` you can launch from the chapter root:

```bash
dotnet run --project 00-Prerequisites
# then pick a demo:
#   [0] Cache line size probe
#   [1] False sharing demo (with and without padding)
#   [2] Context-switch cost
#   [3] NUMA-aware allocation observation
```

The host project wires the demos via `Concurrency.Shared.ConsoleLab`.

## What to read next

- If you've internalised the four facts above → [01-Fundamentals](../01-Fundamentals/).
- If "store buffer" or "MESI" are new → start with [Cache-Coherency](Cache-Coherency/) and [False-Sharing](False-Sharing/) — they build the intuition.
