# ConfigureAwait

Two questions about every `await`:

1. Should I capture the current `SynchronizationContext` so the continuation runs on it?
2. Should I capture the current `ExecutionContext` so AsyncLocal values flow?

`ConfigureAwait(false)` answers "no" to question 1. `.NET 8`'s `ConfigureAwait(ConfigureAwaitOptions)` lets you answer both.

## The classic `ConfigureAwait(false)`

```csharp
await SomethingAsync().ConfigureAwait(false);
```

- Continuations run on whoever finishes the awaited task (typically the pool).
- `ExecutionContext` still flows (AsyncLocal works).

Where it matters:

- **Library code that may run under hostile sync contexts** (legacy ASP.NET, WinForms). Without `(false)`, your library forces continuations onto the UI/request thread; combined with caller `.Result`, that deadlocks.
- **Hot paths** where avoiding the SynchronizationContext.Post is a measurable win.

Where it does **not** matter:

- ASP.NET Core, Console, generic host. There's no sync context to capture.
- App code in those hosts; only library code is exposed to other hosts.

## .NET 8: `ConfigureAwaitOptions`

```csharp
[Flags]
public enum ConfigureAwaitOptions
{
    None                = 0,
    ContinueOnCapturedContext = 1,
    SuppressThrowing    = 2,        // await without rethrowing the task's exception
    ForceYielding       = 4,        // always suspend, even if task is already complete
}
```

Usage:

```csharp
await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
// task may be Faulted; you must check task.Status / task.Exception manually
```

`SuppressThrowing` is the headline feature — it lets you `await` a task purely to *wait for it*, without making the await observe the exception. Useful for graceful-shutdown patterns where you've already logged the failure elsewhere and just need to know the task ended.

`ForceYielding` is what `Task.Yield()` does internally; sometimes you want it on a specific await.

## Common myths

| Myth | Reality |
|---|---|
| "ConfigureAwait(false) is faster" | Marginally and only in hosts with a sync context. In ASP.NET Core, it's a no-op. |
| "ConfigureAwait(false) makes my code thread-safe" | No. It only changes resumption thread. Thread safety is still your problem. |
| "I should always ConfigureAwait(false)" | Not in app code with no sync context. It just adds noise. In libraries: yes. |
| "ConfigureAwait(true) opts in to UI thread" | The default is `true`; you don't need to write it. |
| "ConfigureAwait(false) skips ExecutionContext" | No. Only the SynchronizationContext capture is affected. AsyncLocal still flows. |

## A practical policy

For application code in modern hosts (ASP.NET Core, console, generic host): **don't bother**. The default works.

For library code: **default to `ConfigureAwait(false)`**. Make your library robust under any host. The cost is one ugly call per await; the benefit is no risk of forcing UI continuations.

For test code: **don't use it** unless you've discovered a real issue. Tests usually run on the pool already.

## Roslyn analyser

The `CA2007` analyser warns on missed `ConfigureAwait` calls. It's noisy in app code (where it's pointless) and useful in libraries. Configure it per-project: in `Directory.Build.props`/`.editorconfig`, set severity to `none` for app projects and `warning` for shared library projects.
