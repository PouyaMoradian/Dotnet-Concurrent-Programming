# SemaphoreSlim

The single most-used async-aware synchronisation primitive in modern .NET code. Use it for:

- **Async mutex** (`new SemaphoreSlim(1, 1)`).
- **Concurrency cap** (`new SemaphoreSlim(N, N)`).
- **Bounded resource pool** (rent/return a connection).

## Why `Slim`?

There's also `Semaphore` — a kernel-backed `WaitHandle`. `SemaphoreSlim` is user-mode-fast (a CAS on a counter, optionally backed by a wait-handle for kernel-mode escalation). It does **not** cross processes. If you need cross-process, use `Semaphore`.

## API surface

| Method | Effect |
|---|---|
| `Wait()` | Block until a slot is free |
| `Wait(timeout)` | As above with a timeout |
| `Wait(ct)` | Cancellable wait |
| `WaitAsync(...)` | Async equivalents — *the* reason this primitive exists |
| `Release([count=1])` | Free a slot |
| `CurrentCount` | Number of free slots (only useful for telemetry; racy) |

## Async mutex

```csharp
private readonly SemaphoreSlim _gate = new(1, 1);

async Task DoOnceAtATimeAsync()
{
    await _gate.WaitAsync();
    try
    {
        await DoTheThingAsync();
    }
    finally { _gate.Release(); }
}
```

The `try/finally` is non-negotiable. A skipped `Release` permanently strands a slot.

## Concurrency cap (canonical async fan-out)

```csharp
async Task<List<T>> FanOutAsync(IEnumerable<Uri> urls, int maxParallel)
{
    using var gate = new SemaphoreSlim(maxParallel, maxParallel);
    var tasks = urls.Select(async url =>
    {
        await gate.WaitAsync();
        try { return await client.GetFromJsonAsync<T>(url); }
        finally { gate.Release(); }
    });
    return (await Task.WhenAll(tasks)).ToList();
}
```

In modern .NET, prefer **`Parallel.ForEachAsync`** with `MaxDegreeOfParallelism` for this exact pattern — see [07-TPL/Parallel.ForEachAsync](../../07-Task-Parallel-Library/Parallel.ForEachAsync). The semaphore version is still correct and useful when you need finer control.

## Pitfalls

1. **`Wait()` (sync) on a `SemaphoreSlim` from async code.** Defeats the purpose; can starve the pool if held while continuations need workers. Always `WaitAsync`.
2. **Forgetting the `try/finally`.** A `Release` skipped on exception strands a slot.
3. **`maxCount` mismatch.** `new SemaphoreSlim(0, 5)` starts empty with capacity 5. `new SemaphoreSlim(5)` starts at 5 with no max — `Release` can grow it past 5.
4. **`Release(int)` past the max** throws `SemaphoreFullException`. Always specify both args.
5. **Re-entrance is *not* supported.** It's a counting semaphore, not a recursive mutex. The same task awaiting twice will hang.

## Cancellation

`WaitAsync(CancellationToken)` returns a faulted task on cancellation. Combine with `using var cts = new CancellationTokenSource(timeout)` for timed acquisition.

## Performance

| Op | Approx cost |
|---|---|
| `WaitAsync` no contention (slot free) | ~30 ns |
| `WaitAsync` contention (queue + later resume) | task allocation + event |
| `Wait` no contention | ~20 ns |

For *very* high-frequency micro-locking, a `lock` is faster. SemaphoreSlim's reason for existence is **async**.
