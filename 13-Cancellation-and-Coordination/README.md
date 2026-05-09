# 13 — Cancellation and Coordination

> **Layer:** BCL
> **Reading time:** ~25 minutes
> **Prereq:** [07](../07-Task-Parallel-Library/), [08](../08-Async-Await-Deep-Dive/)

`CancellationToken` is the entire .NET cancellation story. There is no `Thread.Abort` (it's gone, and good riddance). There is no platform-level "stop that work." Cancellation is **cooperative** — code that wants to be cancellable observes the token; code that doesn't, doesn't.

## In-chapter folders

| Folder | Topic |
|---|---|
| [CancellationToken](CancellationToken/) | The type itself; semantics; common idioms |
| [LinkedTokens](LinkedTokens/) | Combining tokens (linked source) |
| [CooperativeCancellation](CooperativeCancellation/) | Designing methods to be cancellable |
| [TimeoutPatterns](TimeoutPatterns/) | Combining timeout + parent cancellation correctly |
| [GracefulShutdown](GracefulShutdown/) | Shutting down a host without losing work |

## The five rules

1. **Accept `CancellationToken`** as a parameter on every async method that does anything stoppable.
2. **Pass it through** to every async call inside.
3. **Throw `OperationCanceledException(ct)`** when you observe cancellation — usually via `ct.ThrowIfCancellationRequested()`.
4. **Don't catch `OperationCanceledException` casually.** Let it propagate; that's the whole protocol.
5. **Always dispose the `CancellationTokenSource`.** It owns OS resources (a timer, possibly a kernel handle).

## API map

```csharp
var cts = new CancellationTokenSource();              // create
var cts = new CancellationTokenSource(TimeSpan...);   // with timeout

cts.Cancel();                                         // signal
cts.CancelAfter(TimeSpan...);                         // schedule
cts.Token.IsCancellationRequested                     // check
cts.Token.ThrowIfCancellationRequested()              // throw if so
cts.Token.Register(() => CleanUp())                   // callback on cancel

var linked = CancellationTokenSource.CreateLinkedTokenSource(t1, t2);
// linked.Token is canceled when *any* of t1, t2 is canceled
```

## The `OperationCanceledException` invariant

When you `await` a task that was cancelled with token `ct`:

```csharp
try { await DoAsync(ct); }
catch (OperationCanceledException oce) when (oce.CancellationToken == ct)
{
    // expected: ct triggered
}
```

The framework sets `oce.CancellationToken` to the token associated with the cancellation. Use it to distinguish "my cancellation" from "someone else's" in nested scenarios.

## What cancellation is NOT

- **Not preemptive.** The token's owner does not interrupt running code; the cancelled code must check.
- **Not a kill switch for blocking IO.** A blocking call (`File.ReadAllBytes`) won't honour your token. Use the async equivalent (`File.ReadAllBytesAsync(ct)`).
- **Not safe to ignore.** A method that takes a `CancellationToken` but doesn't observe it is a contract violation.

## Run

```bash
dotnet run --project 13-Cancellation-and-Coordination
```
