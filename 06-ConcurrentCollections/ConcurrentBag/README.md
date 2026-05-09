# ConcurrentBag&lt;T&gt;

The most-misunderstood concurrent collection. `ConcurrentBag<T>` is *not* a general-purpose multi-producer-multi-consumer container; it's optimised for a very specific pattern.

## Internals

- **Per-thread local lists** (LIFO).
- A thread `Add`s to its own list (no contention).
- A thread `TryTake`s from its own list (LIFO, hot cache).
- If its own list is empty, it **steals** from another thread's list (FIFO from the back).

This is essentially the same architecture as the ThreadPool's worker deques. So `ConcurrentBag<T>` is great when:

- The same threads both produce and consume, and items have no required ordering.
- You're doing parallel work with thread-local accumulators that need eventual aggregation.

## When it's the wrong tool

- **Strict producer/consumer**, especially with FIFO requirements → `ConcurrentQueue<T>` or `Channel<T>`.
- **Producer thread != consumer thread**, with no overlap. The consumer pays the steal cost on every take.
- **Memory-pool / object-pool patterns** → use `ObjectPool<T>` (Microsoft.Extensions.ObjectPool).

## Canonical example

```csharp
var bag = new ConcurrentBag<int>();
Parallel.For(0, 1000, i =>
{
    if (i % 2 == 0) bag.Add(i);
    else if (bag.TryTake(out var x)) bag.Add(x * 2);
});
```

Each pool thread has its own list; same threads add and take. No central contention.

## API

```csharp
bag.Add(item);
bag.TryTake(out var item);
bag.TryPeek(out var item);   // expensive — must search
bag.Count;                   // expensive — sums all lists
```

`Count` and `TryPeek` are O(N) in the worst case because they may need to inspect every per-thread list. Don't call them in hot paths.

## Modern alternatives

For most real-world cases that look like `ConcurrentBag<T>`:

- **`ObjectPool<T>`** for resource pooling.
- **`Parallel.For` with `localInit`/`localFinally`** for thread-local accumulation with explicit aggregation.
- **`Channel<T>` (single reader)** for streaming.

Reach for `ConcurrentBag<T>` only when none of those fit and you've measured a benefit.
