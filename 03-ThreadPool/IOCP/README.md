# IO Completion Ports (IOCP) and the IO completion path

The thing that makes async IO *cheap* on .NET is not `async/await`. It's the OS facilities that let many in-flight IOs share few threads. On Windows, that's IOCP. On Linux, it's `epoll` (and now `io_uring`, which CoreCLR is incrementally adopting).

## IOCP — the Windows model

You associate a handle (file, socket, pipe, named pipe) with an **IO completion port**. When you issue `ReadFile` overlapped, control returns immediately; the OS performs the IO; on completion it queues a *completion packet* to the port. Worker threads call `GetQueuedCompletionStatus` and pull completions to run continuations.

```
   user code              kernel                          IOCP            workers
   ─────────             ──────                         ──────           ─────────
   ReadFile(overlapped) ─────►  begin IO                                
                        ──────  …time passes…
                        ──────  IO finishes ───────────►  enqueue
                                                                          GetQueuedCompletionStatus
                                                                          → run continuation
```

The .NET ThreadPool maintains a small pool of **IO threads** (the `ThreadPool.SetMinThreads` second parameter). They block on the completion port and post continuations to worker threads.

## On Linux: epoll (and io_uring)

Sockets in CoreCLR are managed via a single internal `SocketAsyncEngine` that owns an `epoll` instance and a dedicated event loop thread. When data is ready, it dispatches readiness to the registered async operation. File IO is more variable — depending on the FS / open flags, "async" file IO may still rely on a worker thread.

`io_uring` (Linux 5.1+, mature in 5.10+) offers *true* async file IO. CoreCLR is gradually adopting it where benefit > complexity; in .NET 10 it's used opportunistically for sockets on supported kernels.

## Why this matters for code

- **`async/await` plus `*Async` IO methods is *the* way to scale.** The thread pool stays small; in-flight IO is bounded by the OS, not by thread count.
- **`SocketAsyncEventArgs`-style APIs** allow allocation-free re-use of the IO operation state. .NET 10's `SocketsHttpHandler` and `Kestrel` use this internally.
- **The "IO threads" knob is rarely the right knob.** Modern workloads put almost all work on worker threads (since the IO path's job is just to wake the continuation).

## Practical: don't kill the IOCP advantage

Three classic killers:

1. **Sync-over-async** ([18/SyncOverAsync](../../18-Pitfalls-and-Anti-Patterns/SyncOverAsync)).
2. **`File.OpenRead(...)` without `useAsync: true`** — the FileStream is sync; `ReadAsync` will run on a worker via `Task.Run`.
3. **Wrapping sync IO in `Task.Run`** to "make it async". This is *fake* async — it just pushes the blocking into the pool. Sometimes acceptable, often not.

## Diagnostics

```bash
# Watch IO completions per second (Windows)
typeperf "\.NET CLR LocksAndThreads(*)\Completed work items / sec"

# Per-process IO stats
dotnet-counters monitor System.Net.NameResolution,System.Net.Http,System.Net.Sockets
```
