# Lock-free patterns

A *lock-free* algorithm guarantees that **at least one thread is making progress at all times**. A *wait-free* algorithm guarantees that *every* thread makes progress in a bounded number of steps. Most of what we ship is lock-free, not wait-free; wait-free is academically clean but practically expensive.

## When to reach for lock-free

- Hot paths where lock contention is the bottleneck *and* sharding/per-thread state isn't an option.
- Data structures crossing thread boundaries on every iteration (queues feeding the consumer thread).
- Patterns where holding a lock across an `await` would be ugly and a `SemaphoreSlim` is unsuitable.

For most application code: don't. Use `ConcurrentDictionary`, `Channel<T>`, or `ImmutableDictionary` + atomic-swap. Lock-free is a hot-path tool.

## Pattern 1 — Atomic publication (CoW)

```csharp
public sealed class HotConfig
{
    private static Configuration _current = new();

    public static Configuration Current => Volatile.Read(ref _current);

    public static void Update(Func<Configuration, Configuration> updater)
    {
        Configuration old, next;
        do { old = Current; next = updater(old); }
        while (Interlocked.CompareExchange(ref _current, next, old) != old);
    }
}
```

Readers are **lock-free and allocation-free**. Writers allocate one new `Configuration` per attempt. Use when reads vastly outnumber writes.

## Pattern 2 — Lock-free counter aggregation

```csharp
public sealed class StripedCounter
{
    private readonly long[] _shards;
    private const int Spacing = 16;            // 128 bytes / 8 bytes per long

    public StripedCounter() => _shards = new long[Environment.ProcessorCount * Spacing];

    public void Increment()
    {
        ref var slot = ref _shards[(Environment.CurrentManagedThreadId % Environment.ProcessorCount) * Spacing];
        Interlocked.Increment(ref slot);
    }

    public long Read()
    {
        long total = 0;
        for (var i = 0; i < _shards.Length; i += Spacing)
            total += Volatile.Read(ref _shards[i]);
        return total;
    }
}
```

Writers contend only on their shard's cache line. Reads are slightly more expensive (sum N shards) but lock-free. Beats both `Interlocked.Increment` (cache-line ping-pong) and `lock` (kernel waits) at high concurrency.

## Pattern 3 — Treiber stack

See `Demos/TreiberStackDemo.cs` in this chapter. Lock-free LIFO via head-CAS. Used inside `ConcurrentStack<T>`.

## Pattern 4 — Michael-Scott queue

Lock-free FIFO via head/tail double-CAS. The reference is the 1996 Michael & Scott paper. Notoriously easy to get subtly wrong (the "trailing CAS" detail). The BCL's `ConcurrentQueue<T>` uses a *segment*-based variation that's easier to reason about and has better cache behaviour.

## Pattern 5 — Single-producer-single-consumer (SPSC) ring

The fastest queue type when you have exactly one producer and one consumer. No CAS needed, only `Volatile`:

```csharp
public sealed class SpscRing<T>
{
    private readonly T[] _buf;
    private readonly int _mask;
    private long _head;            // written by producer, read by consumer
    private long _tail;            // written by consumer, read by producer

    public SpscRing(int sizePow2) { _buf = new T[sizePow2]; _mask = sizePow2 - 1; }

    public bool TryEnqueue(T item)
    {
        var head = Volatile.Read(ref _head);
        if (head - Volatile.Read(ref _tail) >= _buf.Length) return false;
        _buf[head & _mask] = item;
        Volatile.Write(ref _head, head + 1);
        return true;
    }

    public bool TryDequeue(out T item)
    {
        var tail = Volatile.Read(ref _tail);
        if (tail >= Volatile.Read(ref _head)) { item = default!; return false; }
        item = _buf[tail & _mask];
        Volatile.Write(ref _tail, tail + 1);
        return true;
    }
}
```

Use cases: in-process pipelines where threads are pinned (HFT, audio).

## When lock-free goes wrong

1. **Memory-ordering bugs.** Write the wrong barrier and your code "works on x86, fails on ARM."
2. **ABA** ([next folder](../ABA-Problem/)).
3. **Live-lock under heavy contention.** A retry loop where every thread keeps invalidating others' attempts. Throw a `SpinWait.SpinOnce()` to back off.
4. **Memory reclamation.** When can you delete a node? In Java/C#, the GC handles this. In native code (or `unsafe` pooled C# code) you need hazard pointers or epoch-based reclamation.

The BCL's lock-free types are state-of-the-art and you should use them. Roll your own only with a benchmark, a stress test, and a co-author who has read [Maurice Herlihy's *The Art of Multiprocessor Programming*](https://www.elsevier.com/books/the-art-of-multiprocessor-programming/herlihy/978-0-12-415950-1).
