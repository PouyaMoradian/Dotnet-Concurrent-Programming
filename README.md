# .NET Concurrent Programming

> The definitive, deep-dive guide to **concurrency, parallelism, and asynchrony in modern .NET (8 / 9 / 10)** — from CPU cache lines and the CLR memory model up to lock-free data structures, channels, dataflow pipelines, and production-grade real-world systems.

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-13.0-239120?logo=csharp)](https://learn.microsoft.com/dotnet/csharp/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](CONTRIBUTING.md)

---

## Why this repository exists

Concurrency is the single most leveraged — and most misunderstood — area of modern systems engineering. A single misplaced `.Result` can deadlock a webserver. A single un-padded shared field can drop throughput by 10×. A single `async void` can crash a process. And yet most developers learn concurrency by accretion: a Stack Overflow answer here, a blog post there, until they have a working but shallow mental model that breaks under load.

This repository is the antidote. It is structured as a **progressive curriculum + working code lab + benchmark suite**. Every folder is:

1. A **chapter** with a deep, opinionated `README.md`.
2. A **runnable .NET project** in the same solution, so every example compiles and runs.
3. Cross-referenced with **BenchmarkDotNet** measurements where claims are made about performance.

If you read this end-to-end and run the labs, you will understand .NET concurrency at the level of a senior runtime engineer: what the JIT, the GC, the thread pool, the OS scheduler, and the CPU itself are *actually* doing when your `await` resumes.

---

## Who is this for?

| Audience | What you'll get |
|---|---|
| **Mid-level engineers** | A complete mental model of `async/await`, `Task`, `Channel<T>`, `lock`, `Interlocked`, and the thread pool — with the pitfalls that production exposes. |
| **Senior engineers** | The CLR memory model, lock-free patterns, hardware intrinsics, allocation-free async, and how to reason about the JIT's reordering rules. |
| **Performance engineers** | BenchmarkDotNet projects, dotnet-trace / PerfView walkthroughs, ETW/EventPipe diagnostics, GC pressure analysis, and false-sharing reproductions. |
| **Architects** | Production patterns: bulkheads, circuit breakers, rate limiters, backpressure-aware pipelines, structured concurrency, graceful shutdown. |
| **Interview prep** | Curated questions in [`19-Appendix/InterviewQuestions`](19-Appendix/InterviewQuestions). |

---

## How to use this repository

1. **Read in order.** Chapters build on each other. The CPU comes before the thread, the thread before `Task`, `Task` before `async`, `async` before `Channel<T>`.
2. **Run the labs.** Every folder is a `.csproj` referenced by `DotnetConcurrentProgramming.sln`. Open the solution; every chapter is `dotnet run`-able.
3. **Reproduce the benchmarks.** The `BENCHMARKS/` folder uses BenchmarkDotNet — never trust a number you didn't measure.
4. **Break things on purpose.** The `18-Pitfalls-and-Anti-Patterns/` chapter contains code designed to deadlock, livelock, leak, and starve. Run it under `dotnet-counters` and watch the symptoms.

```bash
git clone https://github.com/pouyamoradian/DotnetConcurrentProgramming.git
cd DotnetConcurrentProgramming
dotnet restore
dotnet build
# Run any chapter:
dotnet run --project 08-Async-Await-Deep-Dive
# Run a benchmark:
dotnet run -c Release --project BENCHMARKS/ChannelsBenchmarks
```

---

## Curriculum at a glance

```
00 — Prerequisites          (CPU, cache, NUMA, false sharing)
01 — Fundamentals           (concurrency vs parallelism, mutability, visibility)
02 — OS Threading Model     (Windows / Linux schedulers, kernel vs user mode)
03 — ThreadPool             (hill climbing, IOCP, starvation, custom schedulers)
04 — Synchronization        (lock, Monitor, SemaphoreSlim, RWLock, SpinLock, Barrier)
05 — Atomic Operations      (Interlocked, Volatile, CAS, lock-free, ABA)
06 — Concurrent Collections (ConcurrentDictionary, Queue, Bag, Immutable, Frozen)
07 — Task Parallel Library  (Task lifecycle, schedulers, Parallel.ForEachAsync, ValueTask)
08 — Async/Await Deep Dive  (state machines, SyncContext, ExecutionContext, ConfigureAwait)
09 — Channels               (bounded/unbounded, backpressure, actor patterns)
10 — TPL Dataflow           (Buffer/Transform/Action/Batch blocks, production pipelines)
11 — PLINQ                  (partitioning, merge strategies, ordering, perf tuning)
12 — Memory Model           (CLR memory model, CPU reordering, barriers, intrinsics, SIMD)
13 — Cancellation           (CancellationToken, linked tokens, timeouts, graceful shutdown)
14 — Advanced Patterns      (actors, CQRS, reactive, pipelines, bulkheads, rate limiting)
15 — Performance/Diagnostics (PerfView, dotnet-trace/counters, EventPipe, BenchmarkDotNet)
16 — Modern .NET Features   (RateLimiter, TimeProvider, FrozenCollections, NativeAOT)
17 — Real-world Production  (HFT, distributed workers, Kafka, SignalR, telemetry)
18 — Pitfalls               (deadlock, starvation, sync-over-async, async void, etc.)
19 — Appendix               (interview Q&A, cheat sheets, papers, glossary)
BENCHMARKS — measurements  (numbers behind every claim made in this repo)
```

Each chapter has its own table of contents in its `README.md`.

---

## A unified mental model

Most concurrency confusion comes from conflating four different concerns. This repo keeps them strictly separated:

| Layer | Question it answers | Owns |
|---|---|---|
| **CPU & memory** | "What can the hardware actually see and reorder?" | Cache lines, store buffers, memory barriers |
| **OS** | "Who is on the CPU right now?" | Threads, schedulers, context switches, IOCP |
| **CLR runtime** | "How does the platform abstract the OS?" | ThreadPool, GC, JIT, ExecutionContext |
| **Programming model** | "How does my code express concurrency?" | `Task`, `async/await`, `Channel<T>`, `Parallel`, PLINQ |

Every chapter tags itself with which layer(s) it lives in. When you read about `ConfigureAwait(false)`, you are operating at the *programming model* layer — but the *why* lives in `ExecutionContext`, which lives in the *runtime* layer. Confusing the two is the source of 90% of bad async advice on the internet.

---

## .NET 8 / 9 / 10 — what's new and why it matters

This repo is updated for **.NET 10** and uses **C# 13** features throughout. Key concurrency-relevant improvements covered:

| Feature | Version | Chapter |
|---|---|---|
| `System.Threading.Lock` (true reference-type lock) | .NET 9 | [04](04-Synchronization-Primitives) |
| `Task.WhenEach` (streaming WhenAny) | .NET 9 | [07](07-Task-Parallel-Library) |
| `RateLimiter` partitions, sliding/token-bucket | .NET 8 | [16](16-Modern-.NET-Features/RateLimiting) |
| `TimeProvider` (testable time) | .NET 8 | [16](16-Modern-.NET-Features/TimeProvider) |
| `FrozenDictionary` / `FrozenSet` | .NET 8 | [16](16-Modern-.NET-Features/FrozenCollections) |
| `ConfigureAwait(ConfigureAwaitOptions)` | .NET 8 | [08](08-Async-Await-Deep-Dive/ConfigureAwait) |
| Native AOT + async | .NET 8/9 | [16](16-Modern-.NET-Features/NativeAOT) |
| Tier 1 dynamic PGO on by default | .NET 8 | [15](15-Performance-and-Diagnostics) |
| Pooled async state machines (`PoolingAsyncValueTaskMethodBuilder`) | .NET 7+ | [08](08-Async-Await-Deep-Dive/AllocationFreeAsync) |
| `Parallel.ForEachAsync` | .NET 6+ | [07](07-Task-Parallel-Library/Parallel.ForEachAsync) |

---

## How performance claims are made in this repo

> **Rule:** No number is asserted in this repository without a corresponding `BENCHMARKS/` project that produces it.

Every benchmark is:

- Run on **.NET 10** with `-c Release`.
- Profiled with `[MemoryDiagnoser]` so allocations are visible.
- Reported with min/mean/median, std dev, and ratio to a baseline.
- Documented with the host hardware (`dotnet --info` output captured in `docs/benchmark-results/`).

If a number looks wrong, **rerun on your machine**. Concurrency benchmarks are notoriously sensitive to core count, NUMA topology, hyperthreading, OS scheduler version, and even the Windows power plan.

---

## Repository layout (canonical)

```
DotnetConcurrentProgramming/
├── README.md                          ← this file
├── LICENSE                            ← MIT
├── CONTRIBUTING.md                    ← how to add chapters / fix errors
├── global.json                        ← .NET SDK pin
├── Directory.Build.props              ← shared MSBuild props (LangVersion, Nullable, etc.)
├── Directory.Packages.props           ← central package management
├── .editorconfig                      ← formatting rules
├── .gitignore
├── DotnetConcurrentProgramming.sln    ← single solution, all chapters as projects
│
├── docs/                              ← supporting docs / diagrams / cheat sheets
│   ├── architecture/
│   ├── diagrams/
│   ├── cheat-sheets/
│   └── benchmark-results/
│
├── src/                               ← shared infrastructure used by chapters
│   ├── Shared/                        ← helpers, extensions, common types
│   ├── Diagnostics/                   ← EventSource, counters, tracing helpers
│   └── ProductionSamples/             ← longer integrated samples
│
├── tests/                             ← unit / stress / race tests
│   ├── UnitTests/
│   ├── StressTests/
│   ├── ConcurrencyTests/
│   └── RaceConditionTests/
│
├── BENCHMARKS/                        ← BenchmarkDotNet projects
│   ├── ThreadPoolBenchmarks/
│   ├── ChannelsBenchmarks/
│   ├── LockContentionBenchmarks/
│   ├── AsyncBenchmarks/
│   └── AllocationBenchmarks/
│
├── 00-Prerequisites/
├── 01-Fundamentals/
├── 02-OS-Threading-Model/
├── 03-ThreadPool/
├── 04-Synchronization-Primitives/
├── 05-Atomic-Operations/
├── 06-ConcurrentCollections/
├── 07-Task-Parallel-Library/
├── 08-Async-Await-Deep-Dive/
├── 09-Channels/
├── 10-TPL-Dataflow/
├── 11-PLINQ/
├── 12-Memory-Model-and-LowLevel/
├── 13-Cancellation-and-Coordination/
├── 14-Advanced-Patterns/
├── 15-Performance-and-Diagnostics/
├── 16-Modern-.NET-Features/
├── 17-RealWorld-Production-Examples/
├── 18-Pitfalls-and-Anti-Patterns/
└── 19-Appendix/
```

Each numbered folder is a **fully-runnable .NET project** (`<Folder>.csproj`) inside the single solution `DotnetConcurrentProgramming.sln`.

---

## Prerequisites to read this repo

- **.NET 10 SDK** ([download](https://dotnet.microsoft.com/download))
- **A reasonably modern multi-core CPU** — concurrency invariants you can't reproduce on your dev box are still real on production
- Comfortable with C# syntax through C# 12 (records, pattern matching, primary constructors, collection expressions)
- An honest willingness to read disassembly when the chapter demands it

If you are missing any of these, start with [`00-Prerequisites`](00-Prerequisites). It will get you there.

---

## Contributing

This repository is opinionated but not closed. Errors, omissions, and improvements are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md). Pull requests must include:

1. A reproducer for any claim being changed.
2. An updated benchmark if a perf claim changes.
3. Tests for any new pattern in `src/Shared/`.

---

## Further reading

A short list of the canonical references; full bibliography in [`19-Appendix/FurtherReading`](19-Appendix/FurtherReading) and [`19-Appendix/AcademicPapers`](19-Appendix/AcademicPapers).

- Joe Duffy — *Concurrent Programming on Windows* (still the most thorough single book)
- Stephen Toub — countless devblogs.microsoft.com posts (the `async/await` chapters lean heavily on these)
- Maurice Herlihy & Nir Shavit — *The Art of Multiprocessor Programming*
- Paul McKenney — *Is Parallel Programming Hard, And, If So, What Can You Do About It?* (free)
- Memory model: ECMA-335 §I.12.6, plus Vance Morrison's "What every dev must know about multithreaded apps"

---

## License

MIT. See [LICENSE](LICENSE).
