# Async mechanics — what the compiler actually emits

`async`/`await` is *compile-time sugar* over the **Task-based Asynchronous Pattern (TAP)**. There is no async fairy at runtime. The compiler rewrites your method into a state machine, and `await` becomes "if the awaited thing isn't done yet, register a continuation and return; otherwise keep going."

This page is a fundamentals-level sketch. The full deep-dive lives in [08-Async-Await-Deep-Dive](../../08-Async-Await-Deep-Dive/).

## What you write

```csharp
public async Task<int> FetchAndProcessAsync(string url)
{
    var data = await _http.GetStringAsync(url);
    var n = int.Parse(data);
    return n * 2;
}
```

## What the compiler emits (simplified)

```csharp
public Task<int> FetchAndProcessAsync(string url)
{
    var sm = new FetchAndProcessAsyncStateMachine
    {
        _url = url,
        _builder = AsyncTaskMethodBuilder<int>.Create(),
        _state = -1,
    };
    sm._builder.Start(ref sm);
    return sm._builder.Task;
}

private struct FetchAndProcessAsyncStateMachine : IAsyncStateMachine
{
    public int _state;
    public AsyncTaskMethodBuilder<int> _builder;
    public string _url;
    public TaskAwaiter<string> _awaiter;

    public void MoveNext()
    {
        try
        {
            string data;
            if (_state == -1)
            {
                _awaiter = _http.GetStringAsync(_url).GetAwaiter();
                if (!_awaiter.IsCompleted)
                {
                    _state = 0;
                    _builder.AwaitUnsafeOnCompleted(ref _awaiter, ref this);
                    return;          // <-- yield: control returns to caller
                }
                data = _awaiter.GetResult();
            }
            else  // _state == 0
            {
                data = _awaiter.GetResult();
            }

            int n = int.Parse(data);
            _builder.SetResult(n * 2);
        }
        catch (Exception ex)
        {
            _builder.SetException(ex);
        }
    }

    public void SetStateMachine(IAsyncStateMachine sm) { /* … */ }
}
```

The key shape:

- Each `await` is a *jump table entry*. The state machine remembers where it left off via `_state`.
- If the awaited task is already complete (synchronous fast path), `MoveNext` keeps running with no allocation or thread hop.
- If not, the state machine registers itself as a continuation and returns. The thread is now free.
- When the awaited task completes, the runtime calls `MoveNext` again — possibly on a different thread.

That last point is the punchline. **The thread that executes the second half of your `async` method is not necessarily the same thread that started it.** Don't store anything thread-affine across an `await` unless you've planned for it (see chapter [18-Pitfalls](../../18-Pitfalls-and-Anti-Patterns/)).

## Awaitables and awaiters

`await` works on *anything that has a `GetAwaiter` method* whose return type has `IsCompleted`, `OnCompleted`, and `GetResult`. That's it. So `await` is not limited to `Task`. The built-in awaitables in .NET include:

- `Task` / `Task<T>` — the canonical case, heap-allocated continuation tracking.
- `ValueTask` / `ValueTask<T>` — allocation-free if the work is synchronous; backed by a poolable `IValueTaskSource` if not.
- `YieldAwaitable` (from `Task.Yield()`) — schedules a continuation on the current sync context.
- `ConfiguredTaskAwaitable` (from `task.ConfigureAwait(false)`).
- The `WaitAsync` extension on a number of types.
- Your own custom awaiters — anything implementing the awaiter pattern.

`ValueTask` is the most-asked-about of these. The TL;DR: if a method *frequently completes synchronously* (e.g., a cache hit), `ValueTask` lets you skip the `Task` allocation. If it *usually* awaits, `Task` is simpler and equivalent. There are sharp edges: you must not await a `ValueTask` more than once. See [16-Modern-.NET-Features](../../16-Modern-.NET-Features/) and [08-Async-Await-Deep-Dive](../../08-Async-Await-Deep-Dive/).

## The synchronisation context

When a continuation needs to resume, the runtime captures the current `SynchronizationContext` at the moment of the `await` and posts the continuation back to it.

- On the **UI thread** of WPF/WinForms/MAUI, that context is the UI dispatcher. Your continuation resumes on the UI thread.
- On **ASP.NET Core** and **console apps**, the context is `null`. Continuations resume on whatever pool thread happened to be free.
- On **ASP.NET classic (pre-Core)**, the context is the request context, and capturing it can produce the famous `.Result`-induced deadlock.

`ConfigureAwait(false)` says "I don't care about the context — please don't bother capturing it." In library code that might run under any host, prefer it. In application code on ASP.NET Core, it's a no-op. In WPF/WinForms application code that needs to touch the UI after the await, *don't* use it.

## The cheap version of all of this

If the above is too much detail for now, two facts will get you most of the way:

1. `await` is "do the rest of this method when the task finishes, on whatever thread happens to be appropriate."
2. The state machine is allocated on the heap *only* when an `await` actually has to suspend. Synchronous fast paths are nearly free.

The full state-machine accounting lives in chapter 08.
