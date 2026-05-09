# ManualResetEventSlim

A two-state signal: **set** or **unset**. Many threads wait; one (or more) calls `Set()` to release them all. `Reset()` puts it back. Compared with `ManualResetEvent`:

| | `ManualResetEvent` | `ManualResetEventSlim` |
|---|---|---|
| Backed by | Kernel object | User-mode + lazy kernel-event |
| Cross-process | yes | no |
| Uncontended cost | µs | ~10 ns |
| Cancellable wait | no | yes |

Use `Slim` unless you need cross-process. The "Slim" variant is what every modern .NET sync primitive uses internally for parking.

## Patterns

### One-shot start gate

```csharp
using var go = new ManualResetEventSlim(false);
var workers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
{
    go.Wait();
    DoWork();
})).ToArray();

// All 8 threads are parked on Wait. Release them simultaneously:
go.Set();
await Task.WhenAll(workers);
```

This is the *only* easy way to start N tasks at the same instant for benchmarking — `Task.Run` of 8 things does *not* guarantee simultaneous starts.

### Reused phase signal

```csharp
using var phaseStart = new ManualResetEventSlim(false);
// ...
phaseStart.Set();   // release waiters
// ...
phaseStart.Reset(); // arm for next phase
```

Watch out: between `Set` and `Reset`, a fast waiter may complete and come back to wait again — and miss the next `Reset` if you're not careful. For phase signalling, `Barrier` is usually a better fit.

### Cancellable wait

```csharp
try { mre.Wait(cancellationToken); }
catch (OperationCanceledException) { /* shut down */ }
```

`ManualResetEvent` doesn't natively cancel; `Slim` does.

## Async?

No `WaitAsync`. Async signalling is `TaskCompletionSource<bool>`:

```csharp
private readonly TaskCompletionSource<bool> _ready =
    new(TaskCreationOptions.RunContinuationsAsynchronously);

public Task ReadyTask => _ready.Task;
public void Signal() => _ready.TrySetResult(true);
```

Note `RunContinuationsAsynchronously` — without it, `_ready.SetResult(...)` runs continuations *inline* on the calling thread, which can deadlock or stack-overflow under load. Always opt in.

## Costs

- `Wait` (already set): ~10 ns.
- `Wait` (not set, brief): ~30–100 ns of polite spinning before parking.
- `Wait` (long): allocates and uses a kernel event behind the scenes; µs.
- `Set` (no waiters): ~5 ns.
- `Set` (waiters): wakes them via the kernel event.
