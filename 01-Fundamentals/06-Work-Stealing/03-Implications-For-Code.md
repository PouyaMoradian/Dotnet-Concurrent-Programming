# What this means when you write code

The work-stealing scheduler and the hill-climbing controller don't show up in your call sites, but they shape the right way to structure work for the ThreadPool. Five practical rules.

## Rule 1 — spawn freely *from* a worker

A task that runs on a pool worker and queues more tasks via `Task.Run` lands those on its *own* worker's local deque. They'll be picked up LIFO (cache-hot) by the same worker, or stolen FIFO by an idle peer. Both are good.

```csharp
public Task ProcessBatch(IEnumerable<Item> items) =>
    Task.WhenAll(items.Select(item => Task.Run(() => ProcessOne(item))));
```

Issuing 1000 of these from inside a pool task is fine. They distribute across workers naturally.

## Rule 2 — spawning from `Main` lands on the global queue

A `Task.Run` issued from `Main` (which runs on the main thread, not a pool worker) lands the task on the global queue. The next idle worker picks it up. This is also fine — the cost is one extra hop through a globally-coordinated queue, which is negligible for any work that does real computation.

The pathological case is *fork-join inside fork-join*, all the way down. If every level of fork-join issues from `Main` instead of from a worker, you serialise on the global queue at every level. But that's an unusual code shape.

## Rule 3 — don't rely on FIFO ordering across cores

The pool can reorder tasks. A task queued before another can run after it on a different worker. If you need ordering, structure it explicitly: use a `Channel<T>` with a single reader, a TPL Dataflow pipeline, or sequential `await` chaining.

```csharp
// WRONG: assumes the order T1, T2, T3 is preserved
Task.Run(() => DoA());
Task.Run(() => DoB_DependsOnA());
Task.Run(() => DoC_DependsOnB());

// RIGHT: explicit ordering
await DoA_Async();
await DoB_Async();
await DoC_Async();
```

## Rule 4 — keep per-task work non-trivial

The per-task overhead is a few microseconds in steady state. If each task does only a microsecond of real work, the pool spends more time on bookkeeping than on the work. Coalesce.

```csharp
// Wasteful: 1M tasks of 100 ns each
Parallel.For(0, 1_000_000, i => Process(items[i]));

// Better: 16 tasks of ~60 ms each (16 * 60ms ≈ 1s wallclock)
Parallel.For(0, 16, partition =>
{
    int from = partition * (items.Length / 16);
    int to   = Math.Min(items.Length, from + items.Length / 16);
    for (int i = from; i < to; i++) Process(items[i]);
});
```

`Parallel.For`'s default partitioner already does this for you when called on a range; the example above is the manual version for cases (e.g. `Parallel.ForEach` with a custom partitioner) where you control the chunking.

## Rule 5 — don't block on the pool

The hill-climbing controller will eventually inject more workers if you starve it by `.Result`-blocking 200 tasks on the pool. But it injects slowly (one every ~500 ms), so for the first many seconds your app is unresponsive. The defences:

1. Be async all the way down.
2. If you *must* call into sync code that internally awaits, do `Task.Run(...).GetAwaiter().GetResult()` from outside the pool (e.g. from `Main`).
3. For genuinely long-running blocking work, use `Task.Factory.StartNew(work, TaskCreationOptions.LongRunning)` — that gets a dedicated thread without consuming a pool worker.

## A small mental model

Imagine the pool as a kitchen with a fixed number of cooks (the workers). Each cook has a counter of in-progress dishes (the local deque). When a new order comes in:

- If a cook took the order from a customer, they add it to their own counter (LIFO push).
- If the order arrives at the door (global queue), the next free cook grabs it.
- If a cook finishes their counter, they peek at peers' counters and grab an item from the bottom (FIFO steal).
- If everyone's busy, the cook with the most experience (hill-climbing) calls a backup cook.
- If a cook stops working because the soup is still boiling (blocked on IO), they're useless until the soup is done — even though the order's not finished, no one else can advance it.

Most ThreadPool questions resolve to "where in this story is your work going wrong?"

## A diagnostic recipe

When you suspect ThreadPool issues, two free measurements give you ~80% of the answer:

```bash
dotnet-counters monitor -p <pid> System.Runtime threadpool-thread-count threadpool-queue-length threadpool-completed-items-count
```

- **`threadpool-queue-length`** rising and not draining → starvation. Your work is queued faster than workers can pick it up. Usually because workers are blocked on sync IO.
- **`threadpool-thread-count`** climbing past `ProcessorCount * 4` → hill-climbing reacting to starvation. Same root cause as above.
- **`threadpool-completed-items-count`** flat or low while CPU is idle → workers are sleeping when they shouldn't be. Almost always sync-over-async or `.Result` somewhere.

The fix is almost always "stop blocking on the pool", not "configure the pool differently".
