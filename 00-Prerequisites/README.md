# 00 — Prerequisites: how the hardware behaves

> **Layer:** CPU & memory
> **Reading time:** ~30 minutes (this index), ~3 hours (with every deep-dive)
> **You'll be ready to:** read every later chapter without hand-waving "the CPU does this"

If you skip this chapter, every conversation about lock-free code, false sharing, the CLR memory model, or `volatile` becomes a faith exercise. Concurrency *is* a hardware story before it is a language story. The C# you write does not run on an idealised von Neumann machine — it runs on a multi-socket, multi-core, multi-cache, out-of-order, store-buffered, prefetching, branch-predicting beast that will reorder your reads and writes and only pretend to obey the program order *you* wrote.

The aim of this chapter is not to turn you into a CPU designer. It is to give you enough mechanical sympathy that, when chapter 12 says *"a release-store is a half fence on x86 because TSO (Total Store Order — the x86 memory model in which stores never get reordered with other stores) already orders all stores"*, the sentence parses on first read. When chapter 04 says *"replace the contended counter with a per-core counter and aggregate"*, you already know why. When chapter 17 says *"the hot ring buffer's `_head` and `_tail` are pinned to separate cache lines"*, you nod instead of squinting.

---

## How this chapter is organised

Each of the five subfolders below has its own short `README.md` (the overview) plus three to five **deep-dive notes**. Read the overview first; the deep-dives are where the substance lives — diagrams, derivations, references, lab measurements.

| Section | What it teaches | Deep-dive notes |
|---|---|---|
| [CPU-Architecture](CPU-Architecture/) | Pipelines, instruction-level parallelism (ILP), out-of-order execution (OoO), branch prediction, single-instruction-multiple-data (SIMD) vectors, store buffer, reading JIT disassembly | 5 notes |
| [Cache-Coherency](Cache-Coherency/) | The memory hierarchy, the MESI / MOESI cache-coherence protocols, the cost of a write under contention, .NET patterns | 4 notes |
| [Context-Switching](Context-Switching/) | What the OS actually does when it preempts a thread, voluntary vs involuntary switches, scheduler quanta, tooling | 5 notes |
| [NUMA](NUMA/) | Multi-socket and chiplet topology, first-touch allocation, .NET Server GC on NUMA, controlling placement | 5 notes |
| [False-Sharing](False-Sharing/) | The mechanism, where it hides, how to fix it, how to detect it in production | 4 notes |

---

## The five hardware facts every .NET dev needs

The rest of this chapter is the unpacking. Internalise these five and 95% of the surprises in later chapters disappear.

### 1. A "core" is not a "thread"

A modern CPU exposes **logical processors** to the OS. With Intel Hyper-Threading or AMD **SMT** (Simultaneous Multi-Threading), two logical processors share one physical core's execution units. `Environment.ProcessorCount` returns logical processors. Two threads pinned to the same physical core compete for the **ALU** (Arithmetic Logic Unit), the L1, and the load/store buffers — `ProcessorCount` overstates your real parallel headroom by ~2× when you're CPU-bound.

```csharp
Console.WriteLine($"Logical processors: {Environment.ProcessorCount}");
// On Linux:    lscpu | grep -E 'Socket|Core|Thread'
// On Windows:  Get-CimInstance Win32_Processor | Select NumberOfCores, NumberOfLogicalProcessors
// On macOS:    sysctl -n hw.physicalcpu hw.logicalcpu
```

A 16-thread Ryzen 7 7700X has 8 *physical* cores. Eight CPU-bound threads will scale near-linearly; sixteen scale to about 1.3× over eight on most workloads, sometimes less. Always measure.

### 2. Memory is a hierarchy, and you usually live in L1

Approximate latencies on a modern x86 server CPU:

| Level | Capacity (per core) | Latency | Notes |
|---|---|---|---|
| Register | ~32 × 64-bit GP, ~32 × SIMD | 0 cycles | The JIT's allocator decides |
| L1d (data) | 32–48 KB | ~4 cycles | Per physical core |
| L1i (instruction) | 32 KB | ~4 cycles | Per physical core |
| L2 | 512 KB – 2 MB | ~12 cycles | Per physical core |
| L3 (LLC — Last-Level Cache) | 8 – 256 MB | ~40 cycles | Shared across cores in a socket |
| Local DRAM | GBs | ~80–100 ns (~250 cycles) | NUMA-local |
| Remote DRAM | GBs | ~150–200 ns | Crossing socket = QPI/UPI hop (Intel QuickPath Interconnect / Ultra Path Interconnect) |
| NVMe SSD | TBs | ~50–100 µs | Three orders of magnitude past DRAM |

A CPU at 3 GHz executes ~3 instructions per ns when hot in L1. A miss to DRAM is **~250–600 wasted cycles** — enough to do hundreds of arithmetic ops. *Cache locality is throughput*. Most "I optimised the algorithm but didn't get faster" stories are memory-bound problems where the layout was the real constraint.

### 3. Cache is coherent, but coherence has a cost

Modern x86 maintains **cache coherence** across cores using protocols like **MESI** (Modified / Exclusive / Shared / Invalid), **MESIF** (Intel's variant — adds a Forwarder state), and **MOESI** (AMD's variant — adds an Owned state). When core A writes to a line that core B has cached, A must first invalidate B's copy (an **RFO** — Read For Ownership message on the ring/mesh interconnect). The line ping-pongs between caches. This is why:

- Two threads incrementing **the same** `int` (with `Interlocked.Increment`) serialize through the cache, and the throughput collapses with core count.
- Two threads writing **different** `int`s that happen to live on the same 64-byte cache line *also* serialize. That's [false sharing](False-Sharing/) and it's stunningly easy to write by accident.
- One writer + many readers is cheap, because the line can stay in **S**hared state in every reader's cache.

### 4. The CPU and the JIT both reorder your code

What you wrote:
```csharp
_data = ComputeExpensive();
_ready = true;
```

What can be observed by another core: `_ready == true` while `_data == null`. The reasons are layered:

- **Compiler / JIT** may reorder unrelated stores (.NET reorders less aggressively than C++ but it still does).
- **CPU store buffer** lets a core "retire" a store before it becomes globally visible.
- **CPU memory model** (x86 is **TSO** — Total Store Order — which forbids most reorderings; ARM64 is much weaker — almost any reordering is legal until a barrier).

The CLR's own memory model gives you a few rock-solid guarantees, plus `Interlocked`, `Volatile`, and `MemoryBarrier` to insert ordering. We unpack this in [12-Memory-Model-and-LowLevel](../12-Memory-Model-and-LowLevel/). For now: **don't** assume program order across threads, ever.

### 5. The OS will take your CPU away when it wants to

Threads do not own the CPU. The kernel scheduler hands them a *quantum* — a slice of time, typically 1–20 ms — after which it preempts them and runs someone else. The switch itself costs 1–10 µs, plus cache disturbance from the new thread cold-missing on lines the previous one warmed.

The realistic implication: any latency budget tighter than a millisecond cannot tolerate an unexpected preemption. Hot paths in **HFT** (high-frequency trading), audio, and games avoid this by pinning threads to specific cores and setting realtime priorities. Most server code lives one tier up and uses asynchronous wait primitives so the OS *doesn't* have to preempt — the thread voluntarily releases the CPU when it has nothing to do.

---

## A mental model of "what runs where"

```
              your C# source
                    │  (Roslyn, C# compiler)
                    ▼
               IL bytecode
                    │  (RyuJIT, at first call + tiered re-JIT)
                    ▼
          machine code in the code heap
                    │
                    ▼
     ┌──────────────────────────────────┐
     │   logical CPU (the OS sees N)    │
     │     │                            │
     │     ▼                            │
     │  physical core (SMT shares this) │
     │     │                            │
     │     ▼                            │
     │  pipeline (fetch → … → retire)   │
     │     │                            │
     │     ▼                            │
     │  L1d / L1i  ──→  L2  ──→  L3     │
     │                            │     │
     └────────────────────────────│─────┘
                                  ▼
                          DRAM (NUMA-local)
                                  │
                                  ▼  (UPI / IF — Intel Ultra Path Interconnect or AMD Infinity Fabric hop)
                          DRAM (NUMA-remote)
```

Every concept in this chapter lives at one of those layers. When you ask "why is my loop slow?" the right next question is *"which layer is bottlenecking it?"* The answer changes the fix: SIMD widens the pipe at the execution-units layer, sharded counters relieve pressure at the cache-coherence layer, NUMA pinning fixes the DRAM layer.

---

## Run the labs

The chapter ships an executable. Run the picker:

```bash
dotnet run --project 00-Prerequisites
```

Or run a single demo by index, e.g.:

```bash
dotnet run --project 00-Prerequisites -- 2   # context switch cost
```

Or pipe `a` to run all of them sequentially:

```bash
echo a | dotnet run --project 00-Prerequisites
```

The host project wires the demos via `Concurrency.Shared.ConsoleLab`.

### Demos in this chapter

| # | Demo | What it shows |
|---|---|---|
| 0 | `CacheLineProbe` | The stride at which `ns/touch` jumps reveals your effective cache-line size. |
| 1 | `FalseSharingDemo` | Two threads, two unrelated counters; ~5–10× slower when they share a cache line. |
| 2 | `ContextSwitchDemo` | Ping-pong two threads through a `ManualResetEventSlim`; measures one round-trip switch. |
| 3 | `LocalityDemo` | Allocate on many threads, read from rotated threads; observe variance from NUMA placement. |
| 4 | `BranchPredictionDemo` | The classic "sorted vs unsorted array" test — same sum, ~3–6× speed difference from predictability. |
| 5 | `InstructionLevelParallelismDemo` | Single accumulator vs four parallel accumulators — exposes ILP without changing the algorithm. |
| 6 | `MemoryLatencyLadderDemo` | Walk arrays of growing size; watch the latency ladder L1 → L2 → L3 → DRAM. |
| 7 | `ContendedInterlockedDemo` | One shared `Interlocked` counter vs N sharded counters — coherence cost made concrete. |
| 8 | `PrefetchAndStrideDemo` | Sequential vs random access at the same working-set size — same bytes, ~10× difference. |
| 9 | `SimdSpeedupDemo` | Scalar sum vs `Vector<T>` sum — the autovectorised version on .NET 8+ is ~4–8× faster. |

Each demo's source lives in [`Demos/`](Demos/) and is heavily commented to explain *why* it measures what it measures.

---

## How to read this chapter

If you have **30 minutes**: read this README (you are doing it), the [Cache-Coherency overview](Cache-Coherency/), and the [False-Sharing overview](False-Sharing/). Run demos 1, 4, 6, 7.

If you have **two hours**: also read every section's `README.md` and at least one deep-dive note per section. Run every demo. Try to *predict* each output before you run it; the gap between your prediction and the measurement is where you'll learn the most.

If you have **a day**: read every deep-dive. Modify a demo to break it (e.g., remove the padding in `FalseSharingDemo`; add a randomly-mispredicting branch to `BranchPredictionDemo`). When you can explain the change in throughput from first principles, you're done with this chapter.

---

## What to read next

- If the four facts above feel obvious → [01-Fundamentals](../01-Fundamentals/).
- If "store buffer", "MESI", or "RFO" felt new → start with [Cache-Coherency](Cache-Coherency/) and [False-Sharing](False-Sharing/) — they build the intuition fastest.
- If you'd like a memory-model preview before chapter 12 → read [CPU-Architecture/04-Store-Buffer-and-Memory-Ordering.md](CPU-Architecture/04-Store-Buffer-and-Memory-Ordering.md).

---

## Cheat-sheet of terms (used throughout the repo)

- **Logical processor** — what the OS scheduler sees. SMT/Hyper-Threading exposes two per physical core.
- **Cache line** — the unit of transfer between caches and memory. 64 B on x86, 64 B on most ARM, 128 B on Apple Silicon. Coherence works at this granularity.
- **RFO** (Read For Ownership) — the bus message a core sends to invalidate other cores' copies of a line before writing.
- **MESI** — the canonical cache-coherence protocol: Modified, Exclusive, Shared, Invalid. Variants: MESIF (Intel), MOESI (AMD).
- **TSO** — Total Store Order, the x86 memory model. Allows store→load reorderings; forbids the others.
- **Store buffer** — a per-core FIFO of retired stores that haven't yet drained to L1. Loads on the same core can forward from it; loads on other cores can't see it.
- **First-touch** — Linux's default policy that pins a memory page to the NUMA node of whichever thread first writes to it.
- **Quantum** — the time slice the OS scheduler grants a thread before it's eligible for preemption.

Each term reappears with full context in the section where it first earns its keep.
