# CQRS — Command/Query Responsibility Segregation

Split your model into:

- **Commands** that mutate state (writes).
- **Queries** that observe state (reads).

The two have different concurrency profiles:

| Aspect | Commands | Queries |
|---|---|---|
| Throughput | Lower; serialised through the write path | High; lock-free if state is immutable-snapshot |
| Concurrency model | Single-writer (per aggregate) is common | Many readers |
| Failure domain | Must roll back / compensate | Can return stale or fail-fast |
| Locking | Explicit (per-aggregate lock or actor) | Often none (CoW / immutable snapshots) |

CQRS isn't a framework — it's a discipline for *choosing different concurrency primitives* for the two paths.

## Concurrency wins from CQRS

1. **Read scalability**: queries hit an immutable snapshot or a read replica; they don't compete with writers.
2. **Write isolation**: each aggregate (or shard) gets one writer at a time — typically an actor.
3. **Different consistency models**: writes are strongly consistent within an aggregate; reads can be eventually consistent across them.

## A small in-process example

```csharp
// Write side: actor per aggregate
public sealed class OrderActor
{
    private readonly Channel<Command> _mb = Channel.CreateUnbounded<Command>();
    private OrderState _state = OrderState.Empty;

    public Task<TResult> Send<TResult>(Func<OrderState, (OrderState, TResult)> command)
    {
        var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _mb.Writer.TryWrite(new Command(s => { var (next, res) = command(s); _state = next; tcs.SetResult(res); return next; }));
        return tcs.Task;
    }

    private record Command(Func<OrderState, OrderState> Apply);

    public async Task RunAsync(CancellationToken ct)
    {
        await foreach (var c in _mb.Reader.ReadAllAsync(ct)) c.Apply(_state);
    }
}

// Read side: lock-free snapshot
public sealed class OrderQueries
{
    private static OrderViewModel _view = new();
    public OrderViewModel Current => Volatile.Read(ref _view);
    public void Update(OrderViewModel next) => Volatile.Write(ref _view, next);
}
```

The actor publishes updated views (CoW); readers see them lock-free.

## Event sourcing — extreme CQRS

Persist events; rebuild state by replay. Concurrency is dominated by the event store's write semantics (typically optimistic concurrency on a stream version).

In .NET, common stacks: EventStoreDB, Marten, MartenDB on Postgres, or a custom append-only log.

## Distributed CQRS

Across processes:

- Commands go to a write service / topic-partition (Kafka).
- Reads go to a read replica / projection store.
- Eventual consistency window is your latency between event publication and projection update.

## When CQRS is too much

- **Small CRUD apps** with low concurrency: don't.
- **Single-aggregate domains**: just lock the aggregate.
- **Heavy joins between aggregates on read**: CQRS makes this harder unless you build the join into the read model.

CQRS pays off when reads scale very differently from writes (most consumer-facing systems) or when audit / temporal queries are first-class requirements (financial, healthcare, gaming).
