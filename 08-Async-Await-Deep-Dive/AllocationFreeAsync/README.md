# Allocation-free async

For hot async paths (millions of calls per second), the allocations of `async Task<T>` add up:

| Per call (suspending) | Bytes |
|---|---|
| State machine box | ~80–120 |
| Task<T> instance | ~80 |
| Action delegate (cached on box) | ~32 |
| Possible ExecutionContext | ~32 |

Three techniques in increasing aggression let you reach near-zero per-call allocations.

## 1. Return `ValueTask`/`ValueTask<T>`

```csharp
public ValueTask<int> ReadAsync()
{
    if (_buffer.HasData) return new ValueTask<int>(_buffer.NextByte());  // sync path: ZERO alloc
    return new ValueTask<int>(SlowAsync());                              // suspending path: 1 Task alloc
}
```

For workloads where ~80%+ of calls complete synchronously (caches, partial reads from a buffer), this alone removes most allocations. Caveats: `ValueTask` rules of usage — see [07/ValueTask](../../07-Task-Parallel-Library/ValueTask/).

## 2. Pooled state machines (`PoolingAsyncValueTaskMethodBuilder`)

.NET 7+ introduced builders that pool the state machine box. Apply per-method:

```csharp
[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<int>))]
public static async ValueTask<int> ReadAsync()
{
    await Task.Yield();
    return 42;
}
```

Or process-wide:

```bash
DOTNET_SYSTEM_THREADING_POOLASYNCVALUETASKS=1 dotnet run
```

Now even the suspending path doesn't allocate the box (it's rented from a pool and returned on completion).

## 3. Implement `IValueTaskSource`

The most aggressive: write your own awaitable that *is* the state.

```csharp
public sealed class PooledOperation : IValueTaskSource<int>
{
    private ManualResetValueTaskSourceCore<int> _core;     // struct provided by BCL

    public ValueTask<int> Start() { /* kick off */ ; return new ValueTask<int>(this, _core.Version); }

    void Complete(int v) => _core.SetResult(v);
    void Fail(Exception e) => _core.SetException(e);

    int IValueTaskSource<int>.GetResult(short token) => _core.GetResult(token);
    ValueTaskSourceStatus IValueTaskSource<int>.GetStatus(short token) => _core.GetStatus(token);
    void IValueTaskSource<int>.OnCompleted(Action<object?> c, object? s, short t, ValueTaskSourceOnCompletedFlags f)
        => _core.OnCompleted(c, s, t, f);
}
```

`Socket.ReceiveAsync` and `SocketsHttpHandler` use this pattern internally. The `PooledOperation` instance can be reused — when the consumer is done, return it to a pool, increment the version, reuse.

## 4. Source generators / generated awaiters

For the absolute hot path, generate per-call-site awaiters at compile time. Niche; mostly for runtime-internal optimisation.

## When to apply this stack

| Allocation profile | Action |
|---|---|
| < 1 KB/req on hot path | leave it alone |
| 1–10 KB/req from async machinery | switch to ValueTask where most calls sync-complete |
| > 10 KB/req on a measured hot path | enable PoolingAsyncValueTaskMethodBuilder |
| Custom IO library / network proto | implement IValueTaskSource for connections |

For 95% of application code, **`Task<T>` is fine**. These patterns are for libraries that ship to the rest of the world and run hot.

## Measuring

```csharp
[MemoryDiagnoser]
public class HotPathBench
{
    [Benchmark(Baseline = true)] public async Task<int> Plain() { /* … */ }
    [Benchmark]                  public async ValueTask<int> Vt() { /* … */ }
    [Benchmark]                  [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<int>))]
                                public async ValueTask<int> Pooled() { /* … */ }
}
```

The `Allocated` column tells the story. Don't apply these techniques without a benchmark; you'll regret the complexity.
