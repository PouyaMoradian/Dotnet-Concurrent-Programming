# Common confusions and how to recognise them

This page is a field guide to the seven most common async/sync mistakes. None of them are obscure — all of them show up in production code review weekly.

## 1. `.Result` and `.Wait()` on tasks

```csharp
public int GetThing(int id)
{
    return _http.GetIntAsync(id).Result;   // ← block waiting for the task
}
```

What it does: parks the calling thread until the task completes.

Where it breaks:

- **Classic ASP.NET / WPF / WinForms** — the captured `SynchronizationContext` wants to resume on the *same* thread that called `.Result`, but that thread is blocked waiting for the task, which is waiting to resume on that thread… deadlock.
- **ASP.NET Core** — no deadlock (no context), but you've turned an async method into a thread-pinning sync one. The advantages of async vanish.

If you *must* bridge from sync to async, the safer escape hatches are:

- `Task.Run(() => SomeAsync()).GetAwaiter().GetResult()` — runs the async work on the pool, so the deadlock is sidestepped. Still wasteful.
- `AsyncContext.Run` from the `Nito.AsyncEx` library — a nested message pump.

But the right answer is almost always: make the caller async too.

## 2. `async void`

```csharp
public async void OnButtonClick(object s, EventArgs e)
{
    await DoWorkAsync();
}
```

This is fine *only* for event handlers — the framework gives you a `void`-returning shape and you can't change it. Anywhere else, `async void` is a bug:

- **Exceptions** from `async void` propagate to the synchronisation context as unhandled exceptions, crashing the process.
- **You can't await it** — callers have no `Task` to track.
- **Tests can't observe completion** — your test framework returns before the work finishes.

The fix: return `Task` instead. If you need a fire-and-forget, return `Task` and explicitly discard with `_ = MyAsync();` (and log inside, since nothing else will).

## 3. Async over sync (fake async)

```csharp
public Task<string> ReadFileAsync(string path) =>
    Task.Run(() => File.ReadAllText(path));
```

Looks async. Isn't. The work is purely synchronous; you've just dropped it onto a pool thread. The caller's worker thread is freed, yes, but a *different* pool thread is now blocked instead. Net change: zero, plus an allocation.

This is bad library design: the method is *advertising* non-blocking IO and is silently sync-on-a-pool-thread. If the underlying API doesn't have an async version, expose the sync one and let the caller decide whether to `Task.Run` it.

There's one legitimate use: a UI app *specifically wants* to get sync work off the UI thread. There, `Task.Run(() => HeavyCpu())` is correct — the caller is opting in to "move this to the pool" and is not pretending to do non-blocking IO.

## 4. Sync over async

```csharp
public int GetCount() => _service.GetCountAsync().Result;
```

The mirror image of #3. You have an async API and want a sync entry point. Best avoided. Acceptable bridges:

- Top-level `Main` in a console app — use `async Task Main` (.NET 7+ supports this).
- A `Run` method on a console host — `Task.Run(...).GetAwaiter().GetResult()`.

Anything else: rewrite the caller as async.

## 5. `Thread.Sleep` inside `async` methods

```csharp
public async Task PollUntilDone(CancellationToken ct)
{
    while (!await IsDoneAsync(ct))
    {
        Thread.Sleep(1000);   // ← parks the worker
    }
}
```

The fix: `await Task.Delay(1000, ct)`. `Task.Delay` is timer-driven and doesn't park a thread; `Thread.Sleep` does.

## 6. Forgetting to await

```csharp
public async Task DoWork()
{
    SaveAsync();        // ← no await; runs unobserved
    await NotifyAsync();
}
```

`SaveAsync()` returns a `Task` you've discarded. If `SaveAsync` throws, no one observes the exception until the GC finalises the task — which is too late to react. Modern Roslyn analysers flag this (`CS4014`).

The fix: `await SaveAsync()`, or if you genuinely want fire-and-forget, `_ = SaveAsync().ContinueWith(t => Log.Error(t.Exception), TaskContinuationOptions.OnlyOnFaulted);` — and even then, prefer a queue.

## 7. `ConfigureAwait` confusion

The shortest possible version of the rules:

| Where the code runs | Should you use `ConfigureAwait(false)`? |
|---|---|
| **Library code** (unknown caller) | **Yes**, always |
| **ASP.NET Core application code** | Doesn't matter; no context to capture |
| **WPF / WinForms / MAUI application code** | **No** — you usually want to resume on the UI thread |
| **Console application code** | Doesn't matter |
| **xUnit / NUnit test methods** | Doesn't matter (no UI thread) |

What `ConfigureAwait(false)` actually does: it tells the awaiter "don't bother capturing the current synchronisation context; resume on whatever thread is convenient." The performance cost of the capture is small but non-zero; the more important effect is avoiding deadlocks in code paths that have a UI/classic-ASP.NET context.

In .NET 8+ there's also `ConfigureAwait(ConfigureAwaitOptions)` with finer-grained options, including `SuppressThrowing` for awaits that should observe but not rethrow. Niche.

## How to scan code for these

Useful Roslyn analysers are bundled into the Microsoft.VisualStudio.Threading.Analyzers package:

- **VSTHRD002** — synchronously waiting on tasks (`.Result`, `.Wait()`).
- **VSTHRD100** — `async void` outside event handlers.
- **VSTHRD103** — `Thread.Sleep` in async methods.
- **VSTHRD110** — observe `Task` results (no unawaited tasks).
- **VSTHRD200** — name async methods `XxxAsync`.

If you set those to "error" in a new project, you catch all seven mistakes above before they hit CI.
