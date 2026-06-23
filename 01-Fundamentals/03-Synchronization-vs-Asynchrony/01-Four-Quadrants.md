# The four quadrants — sync/async × blocking/non-blocking

People conflate the two axes. They're independent.

|  | Blocking (thread parked) | Non-blocking (thread keeps running or is released) |
|---|---|---|
| **Synchronous** (control returns when done) | `stream.Read()`, `Thread.Sleep`, `lock` | tight CPU loop, `Interlocked.Increment` |
| **Asynchronous** (control returns immediately) | `Task.Run(() => Thread.Sleep(1000))` (**anti-pattern**) | `stream.ReadAsync()`, `Task.Delay`, `await Channel.Reader.ReadAsync` |

## Cell-by-cell

### Sync + blocking — the default

The classic synchronous API: the call returns when the work is finished, and while it's not finished, the OS *parks* the calling thread (removes it from the runnable queue). Most pre-async .NET IO APIs are this — `File.ReadAllText`, `WebClient.DownloadString`, `SqlCommand.ExecuteReader`.

```csharp
var content = File.ReadAllText("data.json");
// The thread sleeps on the disk read; control returns when bytes are in.
```

When to use: short single-threaded programs, command-line tools, code where you genuinely don't have anything else to do.

When to avoid: anywhere you have a thread pool to be polite to.

### Sync + non-blocking — pure compute

The call doesn't return until done, but the thread never sleeps. Examples:

```csharp
long sum = 0;
for (int i = 0; i < 1_000_000; i++) sum += i;

int next = Interlocked.Increment(ref counter);
```

These are "sync" because control returns when the work is finished, and "non-blocking" because the thread never gives up the CPU. Counterintuitive but correct: CPU work is the canonical sync-non-blocking case.

### Async + non-blocking — the modern default

Control returns immediately; completion is signalled via a `Task`. While the work is "in flight", *no thread is parked* — the thread that issued the IO is freed to do something else, and when the IO completes, *some* thread picks up the continuation.

```csharp
var content = await File.ReadAllTextAsync("data.json");
```

This is what `async/await` exists for. Implementing it correctly requires kernel cooperation (IOCP on Windows, epoll/io_uring on Linux). When the IO API supports it, you get genuine non-blocking concurrency. When it doesn't…

### Async + blocking — the anti-pattern

A method returns a `Task` but, internally, blocks a worker thread. Two flavours:

```csharp
// Flavour 1: fake-async wrapper around sync work
public Task<string> GetAsync(string url) =>
    Task.Run(() => httpClient.DownloadString(url));
// Returns a Task, but the work runs on a pool thread blocked on the sync HTTP call.

// Flavour 2: sleep inside an async method
public async Task DelayBadly()
{
    Thread.Sleep(1000);   // <-- blocks the worker
    await Next();
}
```

Both **look** async to the caller. Neither is. The promise is broken: the caller sees a `Task` and reasonably assumes "no worker thread tied up", but a worker thread is parked.

Why this matters: at 1000 concurrent calls, async + non-blocking uses a handful of threads. Async + blocking uses 1000 — exactly the number async was supposed to save you.

## How to tell which quadrant a method is in

Reading C# code, the cues are:

| You see… | It's likely… |
|---|---|
| Return type `void` or `T` | Synchronous |
| Return type `Task` / `Task<T>` / `ValueTask<T>` | Asynchronous |
| `async` keyword | Asynchronous |
| Calls to `Thread.Sleep`, `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` | Blocking |
| Calls to `Task.Delay`, `await foo.SomethingAsync()`, `TaskCompletionSource.SetResult` | Non-blocking |

The danger zone is an `async` method that calls a blocking primitive. Audit those.

## A worked example from the demo

```csharp
// SyncVsAsyncDemo:
// Async path
var asyncTasks = Enumerable.Range(0, 200).Select(_ => Task.Delay(200));
await Task.WhenAll(asyncTasks);
// ~200 ms wall time. No thread is parked on Task.Delay — it's a timer callback.

// Sync path
var syncTasks = Enumerable.Range(0, 200).Select(_ => Task.Run(() => Thread.Sleep(200)));
await Task.WhenAll(syncTasks);
// 200 worker threads get parked. The pool grows under starvation
// (hill-climbing — see chapter 03). Wall time depends on how fast the
// pool can inject threads; typically several seconds.
```

The two look almost identical at the call site. The behaviour is fundamentally different. Run the demo and watch `Process.GetCurrentProcess().Threads.Count` jump.
