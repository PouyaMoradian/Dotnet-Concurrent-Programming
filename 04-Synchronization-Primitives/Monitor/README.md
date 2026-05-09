# Monitor — the class behind `lock`

`Monitor` is the exposed API for the same machinery `lock` uses, plus condition-variable-like primitives (`Wait`, `Pulse`, `PulseAll`).

## Methods

| Method | Use |
|---|---|
| `Monitor.Enter(obj, ref taken)` | Acquire the lock, recording success in `taken` |
| `Monitor.TryEnter(obj, timeout)` | Acquire with a timeout — useful for deadlock-avoidance retries |
| `Monitor.Exit(obj)` | Release |
| `Monitor.Wait(obj)` | Atomically release & wait for `Pulse` |
| `Monitor.Pulse(obj)` | Wake one waiter |
| `Monitor.PulseAll(obj)` | Wake all waiters |

## Why use it directly?

Almost never for plain mutual exclusion (`lock` does that better). The reason `Monitor` is interesting is `Wait`/`Pulse`:

```csharp
class BoundedQueue<T>
{
    private readonly Queue<T> _q = new();
    private readonly object _sync = new();
    private readonly int _cap;

    public BoundedQueue(int cap) => _cap = cap;

    public void Enqueue(T item)
    {
        lock (_sync)
        {
            while (_q.Count == _cap) Monitor.Wait(_sync);
            _q.Enqueue(item);
            Monitor.Pulse(_sync);     // signal one waiter
        }
    }

    public T Dequeue()
    {
        lock (_sync)
        {
            while (_q.Count == 0) Monitor.Wait(_sync);
            var item = _q.Dequeue();
            Monitor.Pulse(_sync);
            return item;
        }
    }
}
```

This is the textbook bounded blocking queue. It's correct, but **don't actually write this**. Use `Channel<T>` (bounded) — it's allocation-aware, async-friendly, and faster. `Monitor.Wait/Pulse` is mostly historical, taught for the model.

## The "always loop on Wait" rule

Notice `while (...) Wait()` not `if (...) Wait()`. Spurious wakeups happen (especially with `PulseAll` — every waiter wakes, but only one will get the resource). Always recheck the condition.

## Costs

`Monitor.Wait` releases the lock, parks the thread on the sync block's wait list, and reacquires on `Pulse`. It crosses the kernel boundary on contention. Each `Wait`/`Pulse` round trip is in the µs range — fine for coordination, not for hot-path signalling. For hot-path signalling, `ManualResetEventSlim` or `SemaphoreSlim` are typically faster.

## Modern alternatives

| You wanted… | Use instead |
|---|---|
| Wait until condition is true | `SemaphoreSlim` for a slot, or a `TaskCompletionSource<T>` |
| Bounded queue | `Channel.CreateBounded<T>` |
| Multiple waiters released at once | `ManualResetEventSlim` (sync) or `TaskCompletionSource<T>.Task` (async) |
| Event with reset | `AutoResetEvent` / `ManualResetEvent` (sync) |
