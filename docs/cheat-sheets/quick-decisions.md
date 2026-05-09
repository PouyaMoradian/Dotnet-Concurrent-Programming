# Quick decision cheat-sheet

When you don't know which primitive to reach for, use this table. Each row links to the chapter that explains why.

## "I need to ____"

| Need | Reach for | Don't reach for | Chapter |
|---|---|---|---|
| Run CPU work in parallel over a collection | `Parallel.ForEach` / PLINQ | `Task.Run` per item | [07](../../07-Task-Parallel-Library), [11](../../11-PLINQ) |
| Run async IO over a collection with a concurrency cap | `Parallel.ForEachAsync` (.NET 6+) | `Task.WhenAll` of `Select(...)` | [07](../../07-Task-Parallel-Library/Parallel.ForEachAsync) |
| Producer/consumer with backpressure | `Channel<T>` (bounded) | `BlockingCollection<T>` | [09](../../09-Channels) |
| Fan-out → fan-in pipeline | TPL Dataflow | hand-rolled `Task.WhenAll` chains | [10](../../10-TPL-Dataflow) |
| Mutate a single counter from many threads | `Interlocked.Increment` | `lock` | [05](../../05-Atomic-Operations) |
| Protect a mutable object from concurrent access | `lock` / `System.Threading.Lock` (.NET 9) | `Monitor.Enter` raw | [04](../../04-Synchronization-Primitives/lock) |
| Throttle async callers | `SemaphoreSlim` | `Mutex` | [04](../../04-Synchronization-Primitives/SemaphoreSlim) |
| Cross-process mutual exclusion | `Mutex` | `lock` | [04](../../04-Synchronization-Primitives/Mutex) |
| Many readers, rare writers | `ReaderWriterLockSlim` (measure!) | always-`lock` | [04](../../04-Synchronization-Primitives/ReaderWriterLockSlim) |
| Cache that must be safe under concurrency | `ConcurrentDictionary<K,V>` | `Dictionary` + `lock` | [06](../../06-ConcurrentCollections/ConcurrentDictionary) |
| Read-mostly map known at build time | `FrozenDictionary` (.NET 8) | `ConcurrentDictionary` | [16](../../16-Modern-.NET-Features/FrozenCollections) |
| Wait for many tasks, fail fast | `Task.WhenAll` + `WhenAny` for cancellation | naive `await` loop | [07](../../07-Task-Parallel-Library), [13](../../13-Cancellation-and-Coordination) |
| Stream results as they complete | `Task.WhenEach` (.NET 9) | manual sort by ContinueWith | [07](../../07-Task-Parallel-Library) |
| Cancel an operation cooperatively | `CancellationToken` | `Thread.Abort` (gone) | [13](../../13-Cancellation-and-Coordination/CancellationToken) |
| Rate-limit a dependency | `System.Threading.RateLimiting` | hand-rolled token bucket | [16](../../16-Modern-.NET-Features/RateLimiting) |
| Wait without burning a thread | `await Task.Delay` | `Thread.Sleep` | [08](../../08-Async-Await-Deep-Dive) |
| Long-running compute outside the pool | `Task.Factory.StartNew(... LongRunning)` | `Task.Run` | [03](../../03-ThreadPool/LongRunning) |
| Coordinate phased parallel work | `Barrier` | hand-rolled `CountdownEvent` | [04](../../04-Synchronization-Primitives/Barrier) |
| Test code that uses time | `TimeProvider` (.NET 8) | `DateTime.UtcNow` direct | [16](../../16-Modern-.NET-Features/TimeProvider) |

## Red flags — stop and think

- `.Result` / `.Wait()` / `.GetAwaiter().GetResult()` on a `Task` in any code that may run under a `SynchronizationContext`. → potential deadlock; see [18/SyncOverAsync](../../18-Pitfalls-and-Anti-Patterns/SyncOverAsync).
- `async void` anywhere except an event handler. → exceptions become process-killers; see [18/AsyncVoid](../../18-Pitfalls-and-Anti-Patterns/AsyncVoid).
- `lock(typeof(X))` or `lock("string literal")`. → cross-AppDomain/cross-assembly leakage; see [04/lock](../../04-Synchronization-Primitives/lock).
- `volatile` on a `long` or a reference written from multiple threads expecting tear-free semantics on 32-bit. → see [05/Volatile](../../05-Atomic-Operations/Volatile).
- Unbounded queues anywhere a producer is faster than a consumer. → memory growth without bound; see [09/Backpressure](../../09-Channels/Backpressure).
