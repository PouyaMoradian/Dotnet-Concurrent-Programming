# `[AsyncMethodBuilder]` and pooled state machines

The C# compiler picks an *AsyncMethodBuilder* type based on the return type of an `async` method (`Task` → `AsyncTaskMethodBuilder`, `Task<T>` → `AsyncTaskMethodBuilder<T>`, `ValueTask` → `AsyncValueTaskMethodBuilder`, etc.).

`[AsyncMethodBuilder(typeof(YourBuilder))]` overrides this for a specific method, letting you pick a custom shape. Most useful application: **pooled state machine boxes**.

## Pooled async method builders (.NET 7+)

```csharp
[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<int>))]
public static async ValueTask<int> ReadAsync(Connection c)
{
    await c.WaitForDataAsync();
    return c.ReadInt32();
}
```

Effect: the state machine box is **rented from a pool** instead of newly allocated. After completion, it's returned to the pool. Allocation per call drops to near zero, even on the suspending path.

## Process-wide opt-in

```bash
DOTNET_SYSTEM_THREADING_POOLASYNCVALUETASKS=1 dotnet run -c Release
```

Applies to **all** `async ValueTask` methods in the process. For a hot service this can be a 10-50% allocation reduction.

## Trade-off: lifetime constraints

Pooled boxes carry the same constraints as `ValueTask` — once consumed, they may be recycled. Don't:

- Await the same `ValueTask<T>` twice.
- Pass it to a fan-out that double-awaits.
- Store it for later.

`Task<T>` (non-pooled) doesn't have this constraint; if you need broader semantics, use `Task<T>` instead.

## Custom builders

You can write a builder that:

- Logs every async transition (for observability).
- Captures the originating activity (for tracing).
- Forces continuations onto a specific scheduler.
- Pools awaiters from a free list.

The signature is detailed and not for the faint of heart — see the runtime's `AsyncTaskMethodBuilder` source for reference. You generally only do this for libraries (e.g., the runtime's own `Socket`-internal builders).

## Diagnostics

`dotnet-counters monitor System.Runtime --counters alloc-rate` before and after enabling pooled builders is the right comparison. Or `[MemoryDiagnoser]` in a BenchmarkDotNet test.

## When pooled isn't worth it

- **Methods that always run synchronously** never hit the pool — there was no allocation to save.
- **Methods that rarely run** — the pool's overhead exceeds the saving.
- **Library code where consumers may misuse `ValueTask`**. Stick to `Task<T>`.

## Related: source-generated awaiters

For the absolute hot path (e.g., the BCL's `Socket.ReceiveAsync`), implementations write *their own awaitable* that *is* the operation state — see [08/AllocationFreeAsync](../../08-Async-Await-Deep-Dive/AllocationFreeAsync/). For application code, the pooled builder is the practical sweet spot.
