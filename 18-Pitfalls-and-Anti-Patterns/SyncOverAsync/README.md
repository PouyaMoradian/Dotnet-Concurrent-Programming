# Sync over async

Calling `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` on a `Task` from synchronous code. The call blocks the current thread until the task completes.

## When it's a bug

In the modern hosts (ASP.NET Core, generic host, console), it doesn't deadlock — but it:

- **Blocks a pool worker** for the full duration.
- **Defeats async's whole point** — the worker can't service other requests.
- **Causes ThreadPool starvation** under concurrent load (see [ThreadPoolStarvation](../ThreadPoolStarvation/)).

In legacy hosts (Old ASP.NET, WinForms, WPF), it **deadlocks** because the captured `SynchronizationContext` is the very thread that's blocked.

## Where it sneaks in

- **Constructors** that have async work (constructors can't be async; people resort to `.Result`).
- **Old interfaces** with sync methods that you have to implement with newer async dependencies.
- **Wrapping** an async API to look sync for "compatibility."
- **Event handlers** (`Click`, etc.) — but those should be `async void` (the *only* legitimate `async void`).

## The fixes, in order of preference

### 1. Make the caller async

```csharp
async Task<IActionResult> Get() => Ok(await GetDataAsync());   // ✅
```

### 2. If the interface forces sync, make it async-friendly

Add `Task<T> GetAsync()` to the interface. Migrate callers. Eventually delete the sync method.

### 3. Use `JoinableTaskFactory` (Microsoft.VisualStudio.Threading)

A library that provides safe sync-over-async semantics in interactive scenarios. Used by Visual Studio. Heavyweight; only when you genuinely cannot make the caller async.

### 4. `Task.Run(...).Result` — the "least bad" sync-over-async

```csharp
public T Sync(Func<Task<T>> factory) => Task.Run(() => factory()).GetAwaiter().GetResult();
```

This *avoids* the deadlock in any host because `Task.Run` runs without a SyncContext. It still blocks the calling thread. Use only when the caller absolutely must be sync; document why.

## Detecting

- Roslyn analyser **CA2012** ("Use ValueTasks correctly") — partial.
- Roslyn analyser **AsyncFixer** (NuGet) — finds many.
- Code review search: grep for `\.Result|\.Wait\(\)|\.GetAwaiter\(\).GetResult\(\)`.

## Common counter-arguments — and why they're wrong

- *"I have to call this from a constructor."* → Move the work to an `InitAsync` or factory method.
- *"My interface is sync."* → Change it. Or implement on a wrapper that's async.
- *"It only happens once at startup."* → Then the perf cost is negligible, but the deadlock risk in legacy hosts is real. Use `Task.Run(...).GetAwaiter().GetResult()` if you must.
- *"It's a console app, no SyncContext."* → True; in console, sync-over-async doesn't deadlock — but it's a code smell that propagates. Make it async anyway.

## The corollary: don't sync-fake an async API

```csharp
public Task<T> GetAsync() => Task.FromResult(GetSync());   // ❌ caller thinks they got async
```

This wraps a sync call in a completed `Task`. Caller awaits, but the work happened on their thread. **Don't do this**; either expose the sync method as sync or actually go async.
