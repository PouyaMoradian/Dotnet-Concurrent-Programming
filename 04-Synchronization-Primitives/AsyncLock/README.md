# AsyncLock — exclusive access across `await`s

C# rejects `lock(...) await ...` because the lock would be acquired by *some* thread but the awaiter could resume on a different one. You need an *async-aware* mutual-exclusion primitive.

## Three options, in order of preference

### 1. `SemaphoreSlim(1, 1)` — built-in

```csharp
private readonly SemaphoreSlim _gate = new(1, 1);

public async Task DoOnceAsync(CancellationToken ct)
{
    await _gate.WaitAsync(ct);
    try { await ActuallyDoItAsync(ct); }
    finally { _gate.Release(); }
}
```

This is the canonical pattern. Pros: no extra dependency. Cons: not re-entrant; you must remember the `try/finally`.

### 2. `Nito.AsyncEx.AsyncLock` (NuGet)

```csharp
private readonly AsyncLock _gate = new();

public async Task DoOnceAsync()
{
    using (await _gate.LockAsync())
    {
        await ActuallyDoItAsync();
    }
}
```

Pros: scope-based release (no `try/finally`). Cons: extra dependency; same non-reentrancy.

### 3. Roll your own

There's almost no reason. The semaphore version is one method; using a custom struct adds complexity without payoff.

## Re-entrance

**Don't expect it.** A method holding the gate that calls back into another method that also tries to acquire will hang. If you need to share state across nested calls, restructure: pass an "I already hold the lock" flag, or split the public/private API where the public method acquires once and calls into private methods that don't.

## Cancellation

`SemaphoreSlim.WaitAsync(ct)` is cancellation-aware. The throw on cancellation correctly leaves the semaphore *not* acquired. Just always pass the token.

## Common bug: forgetting to dispose

```csharp
// ❌
await _gate.WaitAsync();
DoWork();                // throw here → gate is permanently held
_gate.Release();
```

Always `try/finally`. Or use the Nito wrapper that disposes for you.

## Read/write async lock

There is no built-in async `ReaderWriterLockSlim`. Options:

- `Nito.AsyncEx.AsyncReaderWriterLock`.
- Hand-roll on top of two `SemaphoreSlim`s (carefully — this is a known foot-gun).

Most production code that thinks it needs this turns out to need either an *immutable snapshot + atomic swap* (no lock at all) or a `Channel<T>` pattern instead.

## Don't use a `lock` and `await` separately

```csharp
// ❌ subtle bug
T snapshot;
lock (_sync) { snapshot = _state; }     // OK so far
var result = await ProcessAsync(snapshot); // OK
lock (_sync) { _state = result; }       // OK individually but the (read, work, write) is not atomic
```

This is a TOCTOU race: two callers can read the same snapshot, both compute, both write — last write wins. If you need atomic read-compute-write, hold the async lock around the whole thing (with `SemaphoreSlim`).
