# Glossary

Definitions of every term used in this repository.

## A

- **ABA problem** — A CAS sees the same value as before, succeeds, but the value was changed and changed back. Mostly avoided in managed C# because the GC keeps references alive; reappears with object pooling. → [05/ABA-Problem](../../05-Atomic-Operations/ABA-Problem/).
- **Actor** — A unit of state owned by a single task that processes messages from a mailbox. → [09/ActorPatterns](../../09-Channels/ActorPatterns/).
- **Acquire** (memory ordering) — A read after which no later memory op can be reordered before it. `Volatile.Read` is acquire-strength. → [12/CLR-Memory-Model](../../12-Memory-Model-and-LowLevel/CLR-Memory-Model/).
- **AsyncLocal** — Per-async-flow state that flows across awaits. → [08/ExecutionContext](../../08-Async-Await-Deep-Dive/ExecutionContext/).
- **AOT (Ahead-of-Time)** — Compilation at publish time rather than at JIT time. → [16/NativeAOT](../../16-Modern-.NET-Features/NativeAOT/).

## B

- **Backpressure** — A slow consumer slows the producer. Built into bounded channels. → [09/Backpressure](../../09-Channels/Backpressure/).
- **Barrier** — A synchronisation primitive at which N participants meet before any can proceed. Also a memory barrier (instruction). → [04/Barrier](../../04-Synchronization-Primitives/Barrier/), [12/MemoryBarriers](../../12-Memory-Model-and-LowLevel/MemoryBarriers/).
- **Bulkhead** — A concurrency cap on calls to one dependency, isolating its slowness from the rest of the system. → [14/Bulkheads](../../14-Advanced-Patterns/Bulkheads/).

## C

- **Cache coherency** — The property that all CPU caches present a consistent view of memory. Implemented by MESI / MESIF / MOESI. → [00/Cache-Coherency](../../00-Prerequisites/Cache-Coherency/).
- **Cancellation token** — A struct that signals "the work should stop." Cooperative; the work must observe it. → [13/CancellationToken](../../13-Cancellation-and-Coordination/CancellationToken/).
- **CAS (Compare-And-Swap)** — Atomic primitive: if the location equals expected, replace with new. The basis of most lock-free algorithms. → [05/CompareExchange](../../05-Atomic-Operations/CompareExchange/).
- **Channel** — `System.Threading.Channels.Channel<T>`. Async producer/consumer queue. → [09](../../09-Channels/).
- **Circuit breaker** — Resilience pattern that stops calls to a dependency after observed failures, then re-tests cautiously. → [14/CircuitBreakers](../../14-Advanced-Patterns/CircuitBreakers/).
- **Concurrency** — Multiple independent activities in flight (not necessarily simultaneous). → [01/02-Concurrency-vs-Parallelism](../../01-Fundamentals/02-Concurrency-vs-Parallelism/).
- **Context switch** — OS swaps one thread off a CPU and another on. ~1–10 µs typical. → [00/Context-Switching](../../00-Prerequisites/Context-Switching/).
- **Cooperative cancellation** — Code voluntarily checks a cancellation token; preemption is not allowed. → [13/CooperativeCancellation](../../13-Cancellation-and-Coordination/CooperativeCancellation/).

## D

- **Deadlock** — Two threads each waiting for a resource the other holds. → [18/Deadlocks](../../18-Pitfalls-and-Anti-Patterns/Deadlocks/).
- **Dataflow** — `System.Threading.Tasks.Dataflow`; graph-of-blocks pipelines. → [10](../../10-TPL-Dataflow/).

## E

- **EventPipe** — Cross-platform diagnostic event transport. → [15/EventPipe](../../15-Performance-and-Diagnostics/EventPipe/).
- **EventSource** — Class for emitting structured diagnostic events. → [15/EventPipe](../../15-Performance-and-Diagnostics/EventPipe/).
- **ExecutionContext** — Ambient state (AsyncLocal) flowed across async transitions. → [08/ExecutionContext](../../08-Async-Await-Deep-Dive/ExecutionContext/).

## F

- **False sharing** — Two threads writing to different variables on the same cache line; throughput collapses. → [00/False-Sharing](../../00-Prerequisites/False-Sharing/).
- **Fence (memory)** — Instruction that constrains reordering. `Interlocked.MemoryBarrier`. → [12/MemoryBarriers](../../12-Memory-Model-and-LowLevel/MemoryBarriers/).
- **Frozen collection** — Build-once read-many immutable collection optimised for reads. → [16/FrozenCollections](../../16-Modern-.NET-Features/FrozenCollections/).
- **Futex** — Linux fast user-mutex; the kernel primitive behind `Monitor`/`SemaphoreSlim` escalation.

## G

- **GC pressure** — Time spent in garbage collection due to high allocation. → [15/GC-Pressure](../../15-Performance-and-Diagnostics/GC-Pressure/).
- **Graceful shutdown** — Stop accepting work, drain in-flight, clean up. → [13/GracefulShutdown](../../13-Cancellation-and-Coordination/GracefulShutdown/).

## H

- **Happens-before** — Memory-model relation: A happens-before B if any thread observing B is guaranteed to see A's effects.
- **Hazard pointer** — Per-thread published reference that prevents reclamation; alternative to GC for lock-free. → [05/ABA-Problem](../../05-Atomic-Operations/ABA-Problem/).
- **Hill climbing** — ThreadPool's adaptive sizing algorithm. → [03/HillClimbing](../../03-ThreadPool/HillClimbing/).
- **Hyper-Threading / SMT** — Two logical processors per physical core sharing execution units. → [02/HyperThreading](../../02-OS-Threading-Model/HyperThreading/).

## I

- **`Interlocked`** — Class for atomic single-location operations. → [05/Interlocked](../../05-Atomic-Operations/Interlocked/).
- **IOCP (IO Completion Port)** — Windows kernel mechanism for async IO completion notifications. → [03/IOCP](../../03-ThreadPool/IOCP/).
- **Idempotent** — An operation safe to apply more than once.
- **Immutable** — A type whose state cannot change after construction. Trivially thread-safe to read.

## L

- **Linked tokens** — Combining multiple CTs via `CancellationTokenSource.CreateLinkedTokenSource`. → [13/LinkedTokens](../../13-Cancellation-and-Coordination/LinkedTokens/).
- **Lock-free** — Algorithm where at least one thread always makes progress (some thread always succeeds in finite steps).
- **`LongRunning`** — `TaskCreationOptions.LongRunning` — hint to allocate a dedicated thread. → [03/LongRunning](../../03-ThreadPool/LongRunning/).

## M

- **MESI** — Cache coherency protocol with Modified / Exclusive / Shared / Invalid states. → [00/Cache-Coherency](../../00-Prerequisites/Cache-Coherency/).
- **Memory barrier** — see Fence.
- **Memory model** — Contract about what reorderings of memory operations are allowed. → [12](../../12-Memory-Model-and-LowLevel/).
- **Monitor** — The class behind `lock`. Lightweight critical section + `Wait`/`Pulse`. → [04/Monitor](../../04-Synchronization-Primitives/Monitor/).

## N

- **NUMA** — Non-Uniform Memory Access; multi-socket systems where memory is local to one socket. → [00/NUMA](../../00-Prerequisites/NUMA/).
- **Native AOT** — see AOT.

## P

- **Parallelism** — Multiple activities executing simultaneously. → [01/02-Concurrency-vs-Parallelism](../../01-Fundamentals/02-Concurrency-vs-Parallelism/).
- **PLINQ** — Parallel LINQ. → [11](../../11-PLINQ/).
- **POH (Pinned Object Heap)** — Separate heap for pinned objects (.NET 5+).
- **Producer/consumer** — Pattern where some tasks produce items and others consume them. Often via a queue/channel.

## R

- **Race condition** — A bug where the result depends on thread interleaving.
- **Rate limiter** — Bounds operations per unit time. → [16/RateLimiting](../../16-Modern-.NET-Features/RateLimiting/).
- **ReaderWriterLockSlim** — A lock that distinguishes readers (parallel) from writers (exclusive). → [04/ReaderWriterLockSlim](../../04-Synchronization-Primitives/ReaderWriterLockSlim/).
- **Release** (memory ordering) — A write before which no earlier op can be reordered after. `Volatile.Write`.

## S

- **`SemaphoreSlim`** — Async-aware counting semaphore; the workhorse for concurrency caps. → [04/SemaphoreSlim](../../04-Synchronization-Primitives/SemaphoreSlim/).
- **SIMD** — Single Instruction, Multiple Data. → [12/SIMD](../../12-Memory-Model-and-LowLevel/SIMD/).
- **`SpinLock`** — A lock that busy-waits instead of parking. → [04/SpinLock](../../04-Synchronization-Primitives/SpinLock/).
- **`SpinWait`** — A "polite spin" helper. → [04/SpinWait](../../04-Synchronization-Primitives/SpinWait/).
- **Starvation** — A thread or task is permanently denied progress.
- **State machine** — The compiler-generated struct/class that implements `async/await`. → [08/StateMachines](../../08-Async-Await-Deep-Dive/StateMachines/).
- **Structured concurrency** — Discipline where child tasks live within the parent's lexical scope. → [07/StructuredConcurrency](../../07-Task-Parallel-Library/StructuredConcurrency/).
- **Sync over async** — Calling `.Result` / `.Wait` on a Task. Anti-pattern. → [18/SyncOverAsync](../../18-Pitfalls-and-Anti-Patterns/SyncOverAsync/).
- **`SynchronizationContext`** — Abstraction for "where do continuations resume?" → [08/SynchronizationContext](../../08-Async-Await-Deep-Dive/SynchronizationContext/).

## T

- **Task** — `System.Threading.Tasks.Task`; unit of asynchronous work.
- **TaskScheduler** — Decides where Tasks run. → [07/TaskSchedulers](../../07-Task-Parallel-Library/TaskSchedulers/).
- **TPL** — Task Parallel Library. → [07](../../07-Task-Parallel-Library/).
- **TSO (Total Store Order)** — x86's strong memory model.

## V

- **`ValueTask`** — Struct-based alternative to `Task` for hot async paths. → [07/ValueTask](../../07-Task-Parallel-Library/ValueTask/).
- **`Volatile`** — Class with `Read` (acquire) and `Write` (release) static methods. → [05/Volatile](../../05-Atomic-Operations/Volatile/).

## W

- **Wait-free** — Stronger than lock-free: every thread makes progress in a bounded number of steps.
- **Work stealing** — ThreadPool worker takes work from another's queue when its own is empty. → [01/06-Work-Stealing](../../01-Fundamentals/06-Work-Stealing/).
