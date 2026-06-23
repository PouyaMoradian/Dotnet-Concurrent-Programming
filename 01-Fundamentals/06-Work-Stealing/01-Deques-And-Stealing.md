# Deques and stealing — the algorithm

The work-stealing scheduler was published by Blumofe and Leiserson (Cilk, 1995) and has become the default fork-join scheduler in nearly every modern runtime: Java's `ForkJoinPool`, Rust's `rayon` and `tokio`, Go's runtime, and .NET's ThreadPool. The differences between them are mostly tuning; the core algorithm is the same.

## The data structures

```
                  ┌──────────── global queue (FIFO) ──────────┐
                  │ G0 | G1 | G2 | …                          │
                  └────────────────┬──────────────────────────┘
                                   │  (workers consume FIFO from the head)
                                   ↓
        Worker 0 deque                Worker 1 deque                 Worker 2 deque
   ┌──────────────────────┐       ┌──────────────────────┐       ┌──────────────────────┐
   │ T9 ← T8 ← T7 ← T6    │       │ U3 ← U2 ← U1         │       │ (empty)              │
   └─top───────────────bot┘       └─top───────────────bot┘       └──────────────────────┘
     │                  │           │                  │
     │ own LIFO         │ steal     │ own LIFO         │ steal     ↑ thief scans peers'
     │ push/pop         │ FIFO      │ push/pop         │ FIFO        bottoms until one has work
     ↓                  ↑           ↓                  ↑
```

Per worker, in pseudo-code:

```text
deque.push_front(task)      // self-queueing
task = deque.pop_front()    // self-dequeueing — LIFO

task = victim.deque.steal_back()  // foreign steal — FIFO
```

The asymmetry — own LIFO, foreign FIFO — is the punchline. It does three useful things:

1. **Locality.** The task you most recently queued is hot in your L1 cache. Popping it next means execution touches mostly-cached memory. A randomly-stolen task from an idle peer is a cache miss anyway, so the thief doesn't lose locality by taking from the bottom.
2. **Contention.** The victim and the thief touch *different ends* of the deque, so they rarely contend on the same cache line. The metadata for the front and back are typically on different cache lines or padded to be.
3. **Fairness across nested tasks.** In a Cilk-style program, the top of the deque is often a small leaf task; the bottom is often a larger, older task. Thieves taking the bottom take the *largest* available unit of work, which tends to give them enough to chew on before they have to steal again.

## The steal algorithm

A worker that finds its own deque empty does the following:

1. Check the global queue. If there's an item, take it (FIFO).
2. Otherwise, pick a peer worker (typically randomly).
3. Try to steal from the bottom of that peer's deque.
4. If the steal fails (deque was empty, or contention), try another peer.
5. After N failed attempts (configurable, ~100 in the .NET implementation), park the worker and wait for new work to arrive.

The parking step is important. A worker that spins forever burns a core; the pool wants idle workers to sleep so other workers can use the CPU.

## Why steal randomly?

You might think the thief should pick the peer with the most work — minimise the number of steals overall. In practice, picking randomly is faster:

- Tracking "who has the most" requires synchronised counters on every push and pop. That's expensive on the hot path.
- Random selection converges to roughly even load with high probability — analysed in the original Cilk paper.
- The cost of a wrong choice is one failed steal attempt, which is cheap.

## When does this go wrong?

The work-stealing model isn't a panacea. Three failure modes:

### 1. Few-but-fat tasks

If you queue 3 huge CPU tasks on a 16-core machine, work-stealing helps a little (workers steal them quickly) but you don't get more parallelism than the number of independent tasks. The fix is to *recursively* fork: a big task should split itself into smaller ones until they're small enough that the per-task overhead matters. `Parallel.For` does this for you with its partitioner.

### 2. Many tiny tasks

If each task does 100 ns of work, the per-task overhead (queue, pop, run) of perhaps 1-2 µs dominates. Solve a bigger chunk per task — either by manually batching or by configuring `Parallel.For`'s partitioner to use larger chunks.

### 3. Blocking inside a task

A task that calls `Thread.Sleep` or `.Result` parks a *worker*, not a task. The pool doesn't know the worker is "really" idle. Hill-climbing eventually injects more workers, but it takes seconds. This is the canonical "thread pool starvation" — see [03-ThreadPool/Starvation](../../03-ThreadPool/Starvation).

## Where to look in the .NET source

The work-stealing implementation is in `src/libraries/System.Private.CoreLib/src/System/Threading/ThreadPoolWorkQueue.cs` in the [dotnet/runtime](https://github.com/dotnet/runtime) repo. Worth reading once if you're going to do any serious work on the ThreadPool's behaviour. Look for `WorkStealingQueue` and `LocalQueue`.

The implementation has a few neat tricks beyond the textbook algorithm:

- Each worker's deque grows dynamically (starting at 32 slots), so workloads that fork heavily don't pay for a giant per-worker buffer that's never used.
- Stealing is implemented with `Interlocked.CompareExchange` on the deque's `m_headIndex`, making it lock-free.
- The global queue is also a lock-free MPSC structure (multi-producer, single-consumer per pickup).

You don't need to know any of this to use `Task.Run`. You do need to know it to understand why the scheduler does what it does when you profile.
