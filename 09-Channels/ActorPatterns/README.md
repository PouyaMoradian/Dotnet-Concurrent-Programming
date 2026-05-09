# Actor patterns with Channels

The actor model: a unit of state owned by a single task that processes messages from a mailbox in order. No locks needed, because no other task touches the state.

`Channel<TMessage>` is a perfect mailbox.

## The simplest actor

```csharp
public sealed class Counter : IAsyncDisposable
{
    private readonly Channel<int> _mb = Channel.CreateUnbounded<int>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _runner;
    private long _state;

    public Counter()
    {
        _runner = Task.Run(async () =>
        {
            await foreach (var delta in _mb.Reader.ReadAllAsync())
                _state += delta;                 // safe: only this task touches _state
        });
    }

    public ValueTask AddAsync(int n) => _mb.Writer.WriteAsync(n);
    public long Snapshot => Volatile.Read(ref _state);

    public async ValueTask DisposeAsync()
    {
        _mb.Writer.Complete();
        await _runner;
    }
}
```

Many concurrent producers can `AddAsync`. The runner serialises updates. **No `lock`, no `Interlocked`** — because state is *confined*, not shared.

## Patterns over actors

### Request/response (replies via TaskCompletionSource)

```csharp
private readonly Channel<(int delta, TaskCompletionSource<long> reply)> _mb;

public Task<long> AddAndGetAsync(int delta)
{
    var tcs = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
    _mb.Writer.TryWrite((delta, tcs));
    return tcs.Task;
}

// runner:
await foreach (var (delta, reply) in _mb.Reader.ReadAllAsync())
{
    _state += delta;
    reply.TrySetResult(_state);
}
```

Always `RunContinuationsAsynchronously` on the TCS — otherwise the *runner* will execute the caller's continuation inline, which can deadlock or starve.

### Functional updates (commands as deltas)

If you can't enumerate every operation, accept arbitrary update functions:

```csharp
private readonly Channel<Func<TState, TState>> _mb;

public ValueTask UpdateAsync(Func<TState, TState> fn) => _mb.Writer.WriteAsync(fn);

// runner:
await foreach (var fn in _mb.Reader.ReadAllAsync())
    _state = fn(_state);
```

Pure-function updates compose elegantly. Beware: closures may capture mutable state — be disciplined about what `fn` references.

### Hierarchical (parent/child)

A "parent" actor's runner spawns "child" actors. Children have their own mailboxes and lifetimes. Failures bubble up to the parent which decides to restart, escalate, or shut down. This is the structure made famous by Erlang/Akka.

In .NET, build it on top of channels rather than reaching for a framework — most app needs are simple.

## When NOT to use actors

- **Heavy compute** that's better as `Parallel.ForEach`. An actor serialises; you might want parallelism.
- **Read-heavy state** — the actor is a single reader; `ImmutableDictionary` + atomic-swap may serve readers better.
- **Fine-grained state** that's a poor fit for "one mailbox per object" (millions of small actors are expensive in .NET; better in Erlang/Elixir).

## When they shine

- **Coordinator state**: connection state, session state, cache invalidation timers.
- **External-resource gatekeepers**: serial port, single-writer file, vendor SDK.
- **Event-source aggregators**: event-sourced read models that update from a stream.

## A note on Akka.NET / Orleans / Proto.Actor

Full actor frameworks add: location transparency, supervision strategies, persistence, clustering. If you need those, take the framework. If you need "single-threaded state with a queue", channels are smaller and easier to reason about.
