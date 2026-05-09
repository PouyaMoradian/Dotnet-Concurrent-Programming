# 04 — Synchronization Primitives

> **Layer:** CLR runtime + OS
> **Reading time:** ~45 minutes
> **Prereq:** [01](../01-Fundamentals/), [02](../02-OS-Threading-Model/)

This is the *primitives* chapter — the locks, semaphores, events, and signalling mechanisms .NET ships. Each one has a use case, a cost, and an anti-pattern. Most production concurrency code uses three of these (`lock`, `SemaphoreSlim`, `CancellationToken`); the others exist for cases the common ones can't handle.

## The map

| Primitive | Owner concept | Async aware? | Cross-process? | Typical cost (uncontended) |
|---|---|---|---|---|
| `lock(obj)` / `Monitor` | Yes (recursive) | No | No | ~10 ns |
| `System.Threading.Lock` (.NET 9) | Yes (recursive) | No | No | ~10 ns; less GC pressure than `lock(object)` |
| `Mutex` | Yes (recursive) | No | **Yes** | µs (kernel object) |
| `SemaphoreSlim` | No | **Yes** (`WaitAsync`) | No | ~30 ns |
| `Semaphore` | No | No | Yes | µs |
| `ReaderWriterLockSlim` | Reader/writer | No | No | ~30 ns reader, more for writer |
| `SpinLock` | Yes | No | No | <10 ns ideal; can burn CPU |
| `SpinWait` | n/a (yield) | n/a | n/a | n/a |
| `Barrier` | n/a (group rendezvous) | No | No | varies |
| `CountdownEvent` | n/a (countdown) | No | No | varies |
| `ManualResetEventSlim` | Set/reset | No | No | ~10 ns set, ~30 ns wait |
| `AsyncLock` (Nito.AsyncEx) | Yes | **Yes** | No | varies |

## Picking one (decision tree)

```
Need to coordinate threads on this object?
├── Async code path? → SemaphoreSlim.WaitAsync(1,1) (or Nito.AsyncEx AsyncLock)
└── Sync only?
    ├── Single process? 
    │   ├── Many readers, rare writer (and you've measured RWLockSlim helps)? → ReaderWriterLockSlim
    │   ├── Critical section is *literally* a few instructions? → consider Interlocked / SpinLock (rare)
    │   └── Otherwise → lock (or System.Threading.Lock on .NET 9+)
    └── Cross-process? → Mutex (named)

Need to count concurrent slots? → SemaphoreSlim
Need many threads to wait until count reaches 0? → CountdownEvent
Need many threads to meet at a barrier? → Barrier
Need to broadcast "go" once → ManualResetEventSlim
```

## In-chapter folders

Each subfolder has its own README and (most) include code samples. Pick what you need.

| Folder | Topic |
|---|---|
| [lock](lock/) | The `lock` keyword: how it lowers, when to use it, and the new `System.Threading.Lock` |
| [Monitor](Monitor/) | The class behind `lock`; `Monitor.Enter/Exit`, `Pulse`, `Wait` |
| [Mutex](Mutex/) | Cross-process named mutex; OS object, expensive |
| [SemaphoreSlim](SemaphoreSlim/) | Async-friendly counter; the workhorse for concurrency caps |
| [ReaderWriterLockSlim](ReaderWriterLockSlim/) | Many-readers/one-writer; harder to use correctly than people think |
| [SpinLock](SpinLock/) | Lock-free spinning lock; usually wrong but right occasionally |
| [SpinWait](SpinWait/) | The "polite spin" helper used inside almost every .NET sync primitive |
| [Barrier](Barrier/) | Phased parallel computations |
| [CountdownEvent](CountdownEvent/) | Wait until N events have occurred |
| [ManualResetEventSlim](ManualResetEventSlim/) | One-shot or repeated broadcast signalling |
| [AsyncLock](AsyncLock/) | The async-correct way to hold a lock across awaits |

## Key principle: short critical sections

The single best advice for production locking:

> **Hold the lock for as little time as possible.** Compute outside the lock; mutate inside.

```csharp
// ❌ Heavy work under lock
lock (sync) { _state = ExpensiveDeserialise(payload); }

// ✅ Compute outside, swap inside
var next = ExpensiveDeserialise(payload);
lock (sync) { _state = next; }
```

The first version serialises every concurrent caller through `ExpensiveDeserialise`. The second only serialises the assignment.

## Performance tip: contention is the cost

Uncontended locks are cheap (10–30 ns). **Contended** locks are 100–10,000× more expensive because they involve cache-coherence traffic and possibly a kernel wait. If you have a lock that's contended, the right fix is usually *not* a faster lock — it's a different data structure (per-thread state, sharding, lock-free).

## Run

```bash
dotnet run --project 04-Synchronization-Primitives
```
