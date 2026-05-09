# CountdownEvent

A simple primitive: wait until `Signal()` has been called N times.

```csharp
using var done = new CountdownEvent(initialCount: 5);
for (var i = 0; i < 5; i++)
    Task.Run(() =>
    {
        DoWork();
        done.Signal();              // each worker signals once when it's done
    });

done.Wait();                        // returns when all 5 have signalled
Console.WriteLine("All done");
```

## When to use it

- **Fan-out where you don't have a `Task[]` to await.** Workers are kicked off by external events, but you want to know when N have finished.
- **Tests** that need to wait for a precise number of background events.

## Modern alternative

If you control the spawning, prefer `Task.WhenAll`:

```csharp
await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(DoWork)));
```

`CountdownEvent` predates `Task.WhenAll` being the obvious answer. Use it when you genuinely have separate "signal" and "wait" sites.

## Reset / reuse

`Reset(int count)` sets the remaining count to `count` and clears the signalled state. Convenient for reusing the event across phases. Note: any thread currently waiting will not return — `Wait()` watches the *current* signalled state, which `Reset` clears.

## Async?

There's no `WaitAsync` overload. If you need async, wrap with a `TaskCompletionSource<bool>` that gets `SetResult` from the *last* `Signal` callback, or use `SemaphoreSlim`-based patterns. Or just use `Task.WhenAll`.

## Comparison

| Need | Primitive |
|---|---|
| Wait for known set of `Task`s | `Task.WhenAll` |
| Wait for N signals from elsewhere | `CountdownEvent` (sync) or `TaskCompletionSource<int>` patterns (async) |
| Synchronise N threads at a barrier | `Barrier` |
| Single one-shot signal | `ManualResetEventSlim` (sync) / `TaskCompletionSource<T>` (async) |
