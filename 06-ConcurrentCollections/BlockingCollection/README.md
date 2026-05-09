# BlockingCollection&lt;T&gt;

A wrapper around any `IProducerConsumerCollection<T>` (defaults to `ConcurrentQueue<T>`) that adds:

- **Blocking `Take` / `Add`** when empty/full.
- **Bounded capacity** with backpressure.
- **`CompleteAdding`** to signal end-of-stream.
- **`GetConsumingEnumerable()`** for a clean consumer foreach.

## Canonical pattern (legacy)

```csharp
using var queue = new BlockingCollection<int>(boundedCapacity: 100);

var producer = Task.Run(() =>
{
    foreach (var x in source) queue.Add(x);
    queue.CompleteAdding();
});

var consumer = Task.Run(() =>
{
    foreach (var x in queue.GetConsumingEnumerable()) Process(x);
});

await Task.WhenAll(producer, consumer);
```

## Why "legacy"?

`BlockingCollection<T>` is **synchronous-only**. There's no `AddAsync` or async equivalent of `GetConsumingEnumerable`. In an async-first codebase this means:

- Producers/consumers waste pool threads while waiting.
- You can't pass cancellation through async chains naturally.
- Allocations are higher than `Channel<T>`'s.

For new code, **`Channel<T>`** is the right answer. It supports:

- `Channel.CreateBounded<T>` with `FullMode = Wait | DropOldest | DropNewest | DropWrite`.
- `WriteAsync` / `ReadAsync` / `ReadAllAsync` (`IAsyncEnumerable<T>`).
- `Writer.Complete()` / `Reader.Completion` for end-of-stream.
- Single-producer / single-consumer fast paths via constructor options.

## When `BlockingCollection<T>` is still right

- Library/tool code that **must** be sync (legacy hosts).
- You're using a custom `IProducerConsumerCollection<T>` (e.g., a priority queue) and want the blocking semantics.
- Migration in progress; you'll switch to `Channel<T>` later.

## Subtle details

- `BlockingCollection<T>` *can* be backed by `ConcurrentStack<T>` for LIFO. Useful for "freshest first" pools.
- `TryTake(out item, timeout)` is the right way to give consumers a deadline.
- `CompleteAdding()` wakes any waiting `Take` with `InvalidOperationException` on empty. The `GetConsumingEnumerable` form handles this transparently.
- Disposing while a consumer is in `Take` is graceful — `Take` throws `OperationCanceledException`.

## Performance

A `BlockingCollection<int>` over `ConcurrentQueue<int>` is ~2-3× slower than a `Channel<int>` for typical producer/consumer workloads — primarily due to the `SemaphoreSlim` it uses internally for bounded slot availability and item count. The gap widens when the workload is async.
