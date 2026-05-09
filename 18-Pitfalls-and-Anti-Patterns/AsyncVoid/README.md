# `async void`

The single most dangerous async pattern in C#. **Almost always a bug.**

## What `async void` does

```csharp
async void DoStuff()
{
    await SomethingAsync();
    throw new Exception("boom");
}
```

When `SomethingAsync` completes, the `throw` happens on the resuming thread (pool worker, `SynchronizationContext`). There's **no `Task` to observe** — the exception is unhandled and propagates to:

- The `SynchronizationContext.UnhandledException` handler if one exists.
- Otherwise the `AppDomain.UnhandledException` event.
- Otherwise: **process termination.**

Caller code can't `try/catch` it because there's no awaitable to await.

## When `async void` is the right answer

**Event handlers**, where the framework explicitly expects `void`:

```csharp
button.Click += async (sender, e) =>
{
    try { await DoSomethingAsync(); }
    catch (Exception ex) { ShowError(ex); }
};
```

Even here, you must `try/catch` inside the handler. Unhandled exceptions in event handlers are still process-level.

## Anti-patterns

### "Fire-and-forget" via async void

```csharp
public void StartBackgroundWork()
{
    DoBackgroundAsync();   // returns Task; warning: not awaited
}

async void DoBackgroundAsync()    // ❌
{
    await Task.Delay(1000);
    DoStuff();
}
```

If `DoStuff` throws, the process dies. No way to observe. No way to await completion. **Don't.**

The right shape:

```csharp
private readonly List<Task> _bgTasks = [];

public void StartBackgroundWork()
{
    var t = Task.Run(async () =>
    {
        try { await DoBackgroundAsync(); }
        catch (Exception ex) { _logger.LogError(ex, "background work failed"); }
    });
    _bgTasks.Add(t);   // for shutdown drain
}
```

Or, better, a `BackgroundService` so the host manages the lifecycle.

### Constructor-style "fire and forget"

```csharp
class Service
{
    public Service() { _ = InitAsync(); }   // ❌ exceptions vanish
    private async Task InitAsync() { await Task.Delay(10); … }
}
```

Don't. Use a factory: `static async Task<Service> CreateAsync() { var s = new Service(); await s.InitAsync(); return s; }`.

## Detecting

- Roslyn analyser **VSTHRD100** (Microsoft.VisualStudio.Threading.Analyzers) — flags `async void`.
- Roslyn analyser **AsyncFixer** (NuGet) — same.
- Code review: grep for `async void` — every hit is suspicious.

In this repo's `.editorconfig`, `VSTHRD100` is set to `error`. Adopt the same.

## The legitimate uses

- **Event handlers** (UI, WinForms, WPF, Avalonia, MAUI).
- **Top-level test methods** in some test runners (xUnit allows `async Task` and prefers it).

That's it. Anywhere else, `async Task` is the answer — you can always discard the task with `_ = TaskAsync()` if you really mean fire-and-forget. But discarding leaves you no failure observability, so:

```csharp
_ = TaskAsync().ContinueWith(t => Log.Error(t.Exception, "fire-and-forget failed"),
                              TaskContinuationOptions.OnlyOnFaulted);
```

…is the slightly-less-bad shape. Even better: avoid fire-and-forget. Use `BackgroundService` or a queue.
