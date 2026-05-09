# Deadlocks

Two or more threads each holding a resource the other needs. Neither can progress; the system hangs.

## The four conditions (Coffman, 1971)

A deadlock requires all four:

1. **Mutual exclusion** — resources are held exclusively.
2. **Hold and wait** — a thread holds resources while waiting for more.
3. **No preemption** — resources can't be taken away.
4. **Circular wait** — a cycle in the wait graph.

Break any one and you can't deadlock.

## Common shapes in .NET

### 1. Two locks, opposite order

```csharp
// Thread A
lock (left) { lock (right) { … } }

// Thread B
lock (right) { lock (left) { … } }
```

Fix: **always acquire in a global order** (e.g., compare by hash code or a sortable id).

### 2. Re-entrant lock taken from a callback

```csharp
lock (sync)
{
    onChange?.Invoke();      // user code — may call back into us
}
```

If the callback acquires another lock, then *that* code calls into us, and we try to take `sync` again, we re-enter (`Monitor` allows it) — but if the callback was on a *different thread* that already holds the other lock, we deadlock.

Fix: **don't call out under a lock**. Capture the snapshot and call out after release.

### 3. Sync-over-async with captured SynchronizationContext

The classic UI/legacy-ASP.NET deadlock:

```csharp
public ActionResult Index()
{
    var x = SomethingAsync().Result;   // blocks the captured context thread
    return View(x);
}

async Task<int> SomethingAsync()
{
    await Task.Delay(10);              // captures the context; wants to resume on it
    return 42;                         // but the context thread is blocked on .Result → deadlock
}
```

Fix: **don't `.Result` / `.Wait()` async** in any code that runs under a `SynchronizationContext`. Make the caller `async`.

### 4. Async lock + non-async code

You hold a `SemaphoreSlim`-as-mutex while calling synchronous code that re-acquires:

```csharp
await sem.WaitAsync();
try { CallSyncCode(); }
finally { sem.Release(); }

void CallSyncCode()
{
    sem.Wait();   // ← hang. Same task; SemaphoreSlim is not re-entrant.
}
```

Fix: don't re-acquire. Or use a re-entrant lock primitive — but better yet, restructure.

### 5. `Task.WhenAll` while holding a lock taken by one of the tasks

```csharp
lock (sync)
{
    Task.WhenAll(tasks).Wait();   // one of the tasks tries lock(sync)
}
```

Fix: don't await tasks that need a lock you're holding.

## Detection

- **Hang in production**: take a dump (`dotnet-dump collect -p <pid>`), inspect with `dotnet-dump analyze`. `clrstack -all` shows every thread's stack — the deadlock is the cycle of `Monitor.Enter` calls.
- **In tests**: timeout every test that uses concurrency. A test that hangs forever is failing.
- **In the IDE**: VS's "Parallel Stacks" view; WinDbg's `!locks`.

## Prevention principles

1. **Hold one lock at a time** when possible. Most code doesn't need nested locks.
2. **Lock around state, not around calls.** Don't invoke virtual / async / event code under a lock.
3. **Define a lock order** if multiple locks are unavoidable. Document it. Enforce in code review.
4. **Prefer concurrent collections** over locks. They're internally non-blocking or finely striped.
5. **Don't sync-over-async.** Make the call chain async end-to-end.

## TryEnter with timeout — useful as a circuit breaker

```csharp
if (!Monitor.TryEnter(sync, TimeSpan.FromSeconds(5)))
    throw new TimeoutException("possible deadlock");
try { … } finally { Monitor.Exit(sync); }
```

Useful for diagnostics — better to throw than to hang. In production, this often means a timeout on a *latent deadlock* that would have hung forever.
