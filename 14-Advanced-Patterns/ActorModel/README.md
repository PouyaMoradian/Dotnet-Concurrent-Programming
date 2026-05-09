# Actor model

An *actor* owns its state and processes one message at a time from its mailbox. Many actors run concurrently; each is single-threaded internally. State is *confined*, not shared, so locks are unnecessary.

The pattern is covered in detail in [09-Channels/ActorPatterns](../../09-Channels/ActorPatterns/) — channels make a great mailbox.

## When to apply

- **Coordinator state**: WebSocket session, connection pool, cache invalidation timer.
- **External-resource gatekeeper**: serial port, single-writer file, vendor SDK.
- **Stateful aggregator**: per-user state in a chat server, per-room state in a game.

## When not to

- **High-fan-out fine-grained state**: actors per item is expensive in .NET (one Task per actor). Erlang/Elixir handle millions; .NET handles thousands comfortably.
- **Read-heavy state**: an actor is a single reader. `ImmutableDictionary` + atomic-swap serves readers better.

## A library or hand-rolled?

| Need | Tool |
|---|---|
| Local actor with local state, no clustering | hand-rolled on `Channel<T>` |
| Distributed actors, supervision, persistence | Akka.NET |
| Virtual actors with location transparency | Microsoft Orleans |
| Lightweight typed actors | Proto.Actor |

For 80% of "I want a thread-safe stateful object", channels-based hand-rolled actors are the simplest answer.

## Failure handling

Actors should isolate failure: an exception in one actor's processing should not corrupt others. Two patterns:

1. **Restart**: catch the exception in the runner loop, log, reset state to a known-good snapshot, continue. Erlang's "let it crash" + supervisor.
2. **Quarantine**: stop the actor, mark it dead, route subsequent calls to a fallback. Reset by manual operator intervention.

## Communication patterns

- **Tell** (fire-and-forget): `actor.Send(msg)` returns immediately.
- **Ask** (request/response): `actor.Send(msg, replyTcs)`; await `replyTcs.Task`.
- **Pub/sub**: an actor publishes to a `Channel<Event>` that many subscribers consume.

The "Ask" pattern always uses `TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously)` — never the default. Default TCS runs continuations inline on the actor thread → easy deadlock.

## Lifecycle

A clean actor lifecycle:

```csharp
public sealed class ExampleActor : IAsyncDisposable
{
    private readonly Channel<Command> _mb = Channel.CreateUnbounded<Command>();
    private readonly Task _runner;
    public ExampleActor() => _runner = Task.Run(RunAsync);

    public ValueTask Send(Command c) => _mb.Writer.WriteAsync(c);

    private async Task RunAsync()
    {
        await foreach (var c in _mb.Reader.ReadAllAsync())
            try { Handle(c); } catch (Exception ex) { Log(ex); /* policy */ }
    }

    public async ValueTask DisposeAsync()
    {
        _mb.Writer.Complete();
        await _runner;
    }
}
```

`await DisposeAsync()` ensures any in-flight messages are processed before exit.
