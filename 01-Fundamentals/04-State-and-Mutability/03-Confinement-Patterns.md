# Confinement patterns

"Confinement" means: the state is mutable, but only one thread can see it. There is no shared mutable state to race on. This is the rung-3 design from the ladder of safety, and in practice it's the most powerful design lever you have for concurrency-heavy systems.

## ThreadLocal — per-thread instance

```csharp
static readonly ThreadLocal<Random> _rng = new(() => new Random(Environment.TickCount));

public static int Roll() => _rng.Value!.Next(1, 7);
```

Each thread that calls `_rng.Value` for the first time gets a *new* instance from the factory. Each subsequent call returns the same instance for that thread. Other threads don't see it.

Use cases:

- Random number generators (`Random` is not thread-safe).
- Per-thread buffers (`StringBuilder`, byte arrays for parsing).
- Per-thread caches for hot lookups.

Caveats:

- `ThreadLocal<T>` values live until the thread dies. On a ThreadPool that means "approximately forever". If the value is heavy, you may leak memory.
- `AsyncLocal<T>` is *not* the same as `ThreadLocal<T>` — it flows across `await`, which `ThreadLocal` does not.

## `[ThreadStatic]` — per-thread static field

```csharp
[ThreadStatic] static int _depth;

public void Recurse()
{
    _depth++;
    try { /* … */ } finally { _depth--; }
}
```

`[ThreadStatic]` only works on static fields and only for value types or reference initialisations that don't require running a constructor — the field is initialised to `default` per thread. Prefer `ThreadLocal<T>` for most uses; reserve `[ThreadStatic]` for hot paths where the extra indirection of `ThreadLocal<T>` matters.

## Actor pattern via channels

```csharp
public sealed class CounterActor : IAsyncDisposable
{
    private readonly Channel<Command> _mailbox = Channel.CreateUnbounded<Command>();
    private readonly Task _loop;
    private int _state;   // mutable, but only ONE thread (the loop) touches it.

    public CounterActor()
    {
        _loop = Task.Run(async () =>
        {
            await foreach (var cmd in _mailbox.Reader.ReadAllAsync())
            {
                switch (cmd)
                {
                    case Increment: _state++; break;
                    case GetValue g: g.Reply.SetResult(_state); break;
                }
            }
        });
    }

    public ValueTask Increment() => _mailbox.Writer.WriteAsync(new Increment());

    public async Task<int> GetValue()
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _mailbox.Writer.WriteAsync(new GetValue(tcs));
        return await tcs.Task;
    }

    public async ValueTask DisposeAsync()
    {
        _mailbox.Writer.Complete();
        await _loop;
    }

    private abstract record Command;
    private sealed record Increment : Command;
    private sealed record GetValue(TaskCompletionSource<int> Reply) : Command;
}
```

The state `_state` is mutated only inside the loop task. From the outside, callers post messages and receive replies. There is no lock, no `Interlocked`, no volatile read — the state is *physically confined* to one thread of execution at a time.

The trade-off:

- Pro: no lock contention, no memory model concerns inside the actor, easy reasoning.
- Pro: trivially serialised — order of operations is the order of message arrival.
- Con: every operation has the latency of a channel write + dequeue (typically a few microseconds).
- Con: scaling out means more actors. One actor is a serial bottleneck.

This is the Erlang / Akka / Orleans model, in plain C#. See [09-Channels/ActorPatterns](../../09-Channels/ActorPatterns) for variations (request/reply, supervision, sharding).

## Single-writer principle

A weaker but often sufficient version of confinement: many threads can read the state, but only one ever writes it.

Single-writer designs are *much* easier to reason about than multi-writer. They map onto rung 4 of the ladder of safety. They also map onto common architectures:

- A cache where one background task fetches updates and many request threads read.
- A monitor that polls a remote service and exposes the latest snapshot.
- A counter incremented by one consumer (not the producers) on each dequeue.

If your design admits a single writer naturally, take it. The reduction in synchronisation requirements is enormous.

## Per-CPU partitioning (sharding)

When even a single actor is too narrow a pipe, shard the state across N actors and pick which one to talk to based on a stable key:

```csharp
private readonly CounterActor[] _shards;

public Task IncrementFor(int key) =>
    _shards[key % _shards.Length].Increment().AsTask();
```

Each shard is independent. Within a shard, the actor's invariants hold. Between shards, there is no shared state.

This is conceptually the same as Microsoft Orleans' grain placement and is the way you scale stateful concurrent systems horizontally. The catch: cross-shard operations (sum of all counters, transactions across two keys) require explicit coordination.

## Confinement as a discipline, not just a primitive

`ThreadLocal<T>` and an actor are tools, but the bigger lesson is the discipline:

> When you can locate mutable state behind a single thread or a single async loop, you eliminate an entire class of concurrency bugs by construction.

The cost is that you must structure your code that way from the start. Retrofitting confinement onto a codebase full of shared mutable globals is hard. Building it in early is cheap.
