# ConcurrentQueue&lt;T&gt;

A lock-free FIFO queue, optimised for many producers and many consumers.

## Internals

- A **linked list of segments**, each segment a power-of-2 ring buffer of slots.
- Each slot has a **sequence number** that producers and consumers CAS to claim ownership (the *bounded MPMC* technique by Vyukov).
- Head and tail pointers move forward; old segments are freed when fully drained.
- Lock-free in the common case; near-zero allocation for steady-state operation (segments are reused / GC'd).

This is much faster and lower-allocation than the Treiber/Michael-Scott variants. The price is more code complexity inside the BCL.

## API

```csharp
queue.Enqueue(item);
queue.TryDequeue(out var item);
queue.TryPeek(out var item);
queue.Count;            // lock-free, but racy
```

## When to choose it

- Producer/consumer with **multiple producers and/or multiple consumers**.
- You need FIFO order (within each producer; total order across producers is not guaranteed).
- You don't need bounding/backpressure.

If you need bounding or async signalling, **use `Channel<T>`** instead. `ConcurrentQueue<T>` plus your own signalling is correct but more code than `Channel<T>` and usually slower.

## The signalling problem

`ConcurrentQueue<T>` doesn't notify consumers when items arrive. You have to:

- Spin (`while (!q.TryDequeue(...)) SpinWait.SpinOnce();`) — wasteful.
- Use a `ManualResetEventSlim` set on Enqueue — workable but you have to be careful about race-on-empty.
- Use a `SemaphoreSlim` whose count tracks the queue size — the canonical "self-rolled BlockingCollection".

**`Channel<T>` solves all of this for you.** Reach for `ConcurrentQueue<T>` only when you're explicitly bypassing the channel pump (rare) or interoperating with code that already uses it.

## Performance hint

`ConcurrentQueue<T>` uses *tail-relative* enumeration for `foreach`, not a snapshot — items added during iteration may or may not be observed. If you need a snapshot, `ToArray()` once.
