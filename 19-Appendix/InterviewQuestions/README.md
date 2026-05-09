# Interview questions

A curated list. Each has a non-trivial answer; if a candidate has read this repo, they should be able to answer all of them.

## Easy (mid-level filter)

**1. What's the difference between `Task.Run` and `Task.Factory.StartNew`?**

`Task.Run` defaults to `TaskScheduler.Default` (the pool) and `TaskCreationOptions.DenyChildAttach`. `StartNew` lets you pick the scheduler and creation options — most callers misuse it (defaulting to `TaskScheduler.Current`, which can be a UI scheduler). Use `Task.Run` unless you need a specific scheduler or `LongRunning`.

**2. What does `await` actually do?**

The compiler rewrites the method into a state machine. On an incomplete awaitable, it captures the current `SynchronizationContext` and `ExecutionContext`, registers the rest of the method as a continuation, and returns the in-progress `Task`. When the awaitable completes, the continuation runs (on the captured sync context, or on the pool worker that finished the task).

**3. Difference between `Task` and `Thread`?**

`Thread` is an OS scheduling unit (~1 MB stack each, kernel-managed). `Task` is a unit of work scheduled by a `TaskScheduler` — typically the ThreadPool. Tasks are cheap (microseconds to start); threads are expensive (hundreds of microseconds).

**4. What is `ConfigureAwait(false)` and when do you use it?**

It tells the awaiter not to capture the current `SynchronizationContext`. In ASP.NET Core / console (no sync context), it's a no-op. In libraries that may run under a captured context (legacy ASP.NET, WinForms), use it to avoid forcing continuations onto the UI/request thread — and to avoid sync-over-async deadlocks.

## Medium

**5. Why is `lock(typeof(SomeType))` an anti-pattern?**

`Type` objects are interned per-AppDomain and shared across assemblies. Anyone else can lock on the same `Type`, leading to surprising contention or deadlock. Always lock on a private `readonly object` (or `Lock`) field.

**6. How does the .NET ThreadPool decide when to add or remove threads?**

The "hill-climbing" controller. Every ~500 ms it perturbs the worker count (±1) and observes throughput. Positive correlation → continue; negative → reverse. A small randomisation breaks symmetry.

**7. What does `volatile` (the keyword) do, and what doesn't it do?**

`volatile` on a field means each read is treated as `Volatile.Read` (acquire) and each write as `Volatile.Write` (release). It does **not** apply to `long`/`double` on 32-bit (no torn-read protection). It does **not** make composed operations atomic. Prefer explicit `Volatile.Read`/`Write` over the keyword.

**8. Difference between `Volatile`, `Interlocked`, and `lock`?**

`Volatile` provides ordering (acquire/release) but not atomicity for composed ops. `Interlocked` provides atomic single-location ops (CAS, increment, exchange) plus full-fence ordering. `lock` provides a critical section over arbitrary code, with fairness via the OS sync primitive on contention.

**9. What happens if a `TaskCompletionSource<T>` `SetResult` is called and many tasks await it?**

By default, all continuations run *synchronously inline on the SetResult caller's thread*. This can deadlock or stack-overflow. Always construct with `TaskCreationOptions.RunContinuationsAsynchronously`.

**10. Why is `Parallel.ForEach` over async work an anti-pattern?**

`Parallel.ForEach` runs body delegates synchronously on pool workers. If the body does `Task.Result` or similar, each iteration pins a worker for the full IO duration, defeating the purpose of async. Use `Parallel.ForEachAsync` instead.

## Hard

**11. Explain the ABA problem and a real-world fix.**

A CAS sees the same value as before, so it succeeds, but the value changed and changed back — usually because nodes are recycled. The standard fix is a *tagged pointer*: pack `(reference, version)` into a struct and CAS on it. Each push increments the version. In managed C#, ABA is rare because the GC keeps a node alive while any reference exists; pooling reintroduces it.

**12. What's the .NET memory model in one sentence?**

Pointer-sized aligned reads/writes are atomic; `Volatile.Read` is acquire; `Volatile.Write` is release; `Interlocked.*` is full-fenced; locks acquire on entry and release on exit. The rest can be reordered by the JIT and CPU.

**13. Walk through what allocations happen on a single `await`.**

If the awaited task is already complete: nothing. If it suspends: the state machine struct is boxed (one allocation), an `Action` for `MoveNext` may be created and cached on the box, the `Task<T>`'s completion source allocates a result holder, and `ExecutionContext` may be cloned if it changed. Pooled async builders eliminate the box; `ValueTask` for sync-completion paths eliminates the Task allocation.

**14. How would you implement an async-correct mutex without using `SemaphoreSlim`?**

Build on `TaskCompletionSource<T>` with `RunContinuationsAsynchronously`. Maintain a queue of waiters; on release, `SetResult` the next waiter's TCS. Be careful around cancellation — a waiter that's cancelled mid-queue must be removed without leaking the queue slot. The Nito.AsyncEx library does this; rolling your own is rarely worth it.

**15. Describe a production scenario where ConcurrentDictionary's `GetOrAdd` factory ran more than once.**

Many threads simultaneously call `GetOrAdd("key", factoryThatTakes100ms)`. The dictionary lets multiple factories run; only one return value wins (the others are discarded). If the factory has side effects (registering with another component, opening a connection, logging), you've now done it more than once. Wrap in `Lazy<T>` to make the multiplicity harmless.

**16. Your service is showing increasing P99 latency under load while CPU is at 30%. What do you investigate?**

Likely ThreadPool starvation. Run `dotnet-counters monitor` and look at `threadpool-thread-count`, `threadpool-queue-length`. If the count is climbing and queue is growing, you have starvation. Capture a wall-time trace; investigate where threads are blocked. Common cause: sync-over-async; less common: blocking IO in async paths, long-running tasks without `LongRunning`, min threads too low for cgroup limits.

**17. Why might `volatile bool` be insufficient for a "stop running" flag?**

It's sufficient for a simple polling loop — `Volatile.Read` ensures freshness. It's *not* sufficient if there's any other state being read along with the flag and the consumer expects ordering. Also: the C# `volatile` keyword on `bool` is fine; the gotcha is for `long`/`double` on 32-bit.

**18. What's the cost of a context switch on Linux?**

Lower bound ~1 µs (saving + restoring registers, kernel-mode entry/exit, scheduler decision). Realistic 2–10 µs including cache disturbance. Cross-NUMA can push it higher.

**19. How do you safely cancel a `Task.Run(() => HeavyComputeAsync())`?**

Pass a `CancellationToken` into `Heavy` and observe it inside. `Task.Run`'s own CT only cancels the *queued* task before it starts; once running, the body must cooperate. For non-cooperative bodies, you can't cancel without abandoning the work — which still runs to completion in the background.

**20. When would you reach for `Channel<T>` over TPL Dataflow?**

`Channel<T>` for: simple producer/consumer, max throughput, async-native iteration, low allocation. Dataflow for: graph composition, per-stage parallelism, predicate-routed `LinkTo`, batching/joining out of the box. If the answer is "I just need a queue," Channel wins. If you have 4+ stages with different parallelism, Dataflow wins on readability.
