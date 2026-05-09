# Event loop

A *single-threaded* execution model: one thread reads from a queue and dispatches handlers in order. Familiar from JavaScript/Node.js. Useful in .NET for:

- **Single-threaded apartments** (legacy COM, some game engines).
- **Reproducibility** in simulations.
- **Tests** that need deterministic ordering.

## The minimal event loop

```csharp
public sealed class EventLoop : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback, object?)> _queue = new();
    private readonly Thread _thread;

    public EventLoop()
    {
        _thread = new Thread(Run) { IsBackground = true };
        _thread.Start();
    }

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));
    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException("only Post supported");

    private void Run()
    {
        SetSynchronizationContext(this);
        foreach (var (d, s) in _queue.GetConsumingEnumerable())
        {
            try { d(s); } catch (Exception ex) { Console.Error.WriteLine(ex); }
        }
    }

    public void Dispose() => _queue.CompleteAdding();
}
```

When you set this as the current `SynchronizationContext`, all `await` continuations resume on the loop thread. `Task.Run` continuations resume on the pool because pool work doesn't capture sync context (its callsite has none).

## When you'd build one

- A **vendor SDK** demands all calls on a single thread.
- **Deterministic test harness** for async code.
- **Game / simulation tick loop**.

## Modern alternative

Often a `Channel<Func<Task>>` plus one consuming task is enough:

```csharp
var loop = Channel.CreateUnbounded<Func<Task>>();
_ = Task.Run(async () =>
{
    await foreach (var work in loop.Reader.ReadAllAsync()) await work();
});

await loop.Writer.WriteAsync(async () => { /* runs on the loop's thread context */ });
```

Without the SynchronizationContext machinery — but also without the implicit "all my awaits go here" benefit. Trade-off.

## Performance

Event loops *serialise* work — they're slower than parallel processing for parallelisable workloads. Their value is *ordering* and *single-threaded invariants*, not throughput.

## Practical advice

If you're tempted to write your own event loop in 2026 .NET, ask:

1. Could `await` + `ConfigureAwait(false)` everywhere give me what I need without a loop? Often yes.
2. Could a `Channel<Command>` actor give me the single-threaded invariants without involving SynchronizationContext? Almost always yes.
3. If the answer to both is no, it's a custom event loop. Write it carefully — bugs in the loop affect every consumer.
