# SynchronizationContext

The `SynchronizationContext` is the abstraction that decides "where should this continuation run?" Each thread can have one (`SynchronizationContext.Current`). The hosts you'll meet:

| Host | `Current` is… | Behaviour |
|---|---|---|
| ASP.NET Core | `null` | continuations run on whoever finishes the awaited task (typically the pool) |
| Console / generic host | `null` | same |
| WinForms | `WindowsFormsSynchronizationContext` | continuations run on the UI thread |
| WPF | `DispatcherSynchronizationContext` | UI thread (via `Dispatcher`) |
| Old ASP.NET (Framework, classic pipeline) | `AspNetSynchronizationContext` | per-request; serialised; the deadlock host |
| Test frameworks (xUnit, MSTest) | varies | xUnit's default has a custom context that funnels resumes |
| Vendor SDKs | varies | sometimes single-threaded apartments |

## How `await` interacts with it

By default, `await` captures `SynchronizationContext.Current` *before* the awaited task is queued. When the task completes, the continuation is `Post`ed to that captured context.

```csharp
// In a WinForms event handler, this thread has the UI sync context.
await client.GetStringAsync(url);   // suspends; UI sync context captured
TextBox1.Text = "done";              // resumes on UI thread → safe
```

If you `ConfigureAwait(false)`, the capture is skipped:

```csharp
await client.GetStringAsync(url).ConfigureAwait(false);
TextBox1.Text = "done";              // ❌ may not be UI thread → INVALID
```

## Why "async deadlock" with `.Result` happens (Old ASP.NET / WinForms)

```csharp
public string Get()                                  // sync action
{
    var result = GetAsyncStuff().Result;             // blocks the UI/request thread
    return result;
}

public async Task<string> GetAsyncStuff()
{
    await Task.Delay(100);                            // captures UI context
    return "done";                                    // wants to resume on UI thread
}
```

The UI thread is *blocked* by `.Result`. The continuation cannot resume on it. Deadlock.

ASP.NET Core does **not** have this hazard because it has no per-request `SynchronizationContext`. Continuations resume on the pool, which is not blocked.

## Custom contexts you might write

Rarely. Reasons might include:

- Bridging async to a custom event loop (game engines, simulation).
- Single-threaded test harness (TestSync).
- Wrapping a vendor SDK that demands "all calls on this thread."

The minimal API:

```csharp
public sealed class SingleThreadedContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback, object?)> _queue = new();
    private readonly Thread _thread;
    public SingleThreadedContext() { _thread = new Thread(Pump) { IsBackground = true }; _thread.Start(); }
    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));
    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();
    private void Pump()
    {
        SetSynchronizationContext(this);
        foreach (var (d, s) in _queue.GetConsumingEnumerable()) d(s);
    }
    public void Dispose() => _queue.CompleteAdding();
}
```

In practice, you'll almost never write one. .NET 10's `Channel<T>`-based loops are simpler and clearer.

## Practical advice

1. **In ASP.NET Core / Console**: `ConfigureAwait(false)` is *aesthetically* a no-op — there's no context to capture. Some teams still mandate it in libraries to make those libraries safe to use under hostile contexts (legacy ASP.NET, WinForms). If your code is "library-grade" and may run elsewhere, use `(false)`.
2. **In UI code**: don't `ConfigureAwait(false)` and then touch UI controls. The capture exists for a reason.
3. **In tests**: be aware that the test harness may have a sync context (xUnit's pre-2.5; recent xUnit doesn't, by default).
