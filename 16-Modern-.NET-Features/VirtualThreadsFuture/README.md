# Virtual threads — speculation about .NET's future

Java's Project Loom introduced **virtual threads** in JDK 21 — green threads that look like OS threads to the programmer but are scheduled cooperatively by the runtime, so millions can exist concurrently.

.NET's equivalent is **already here, sort of**: `async`/`await` plus `Task` gives you the same scaling property — millions of in-flight async operations on a small thread pool. The cost is the syntactic and semantic ceremony of `async`/`await`.

## The Loom pitch (for context)

- Write blocking-style code (`socket.Read()` looks blocking).
- The runtime detects the block, suspends the virtual thread, schedules another.
- No "coloured functions" (no `async` keyword).

## Why .NET doesn't have it

`async`/`await` predates Loom by a decade and works well, but it's *colour-aware*: a method either is or isn't `async`, and the syntax forces you to pick. The advantages: explicit suspension points (predictability), clear error propagation, no surprise re-entrancy.

The disadvantages: viral propagation, and "I have a synchronous library and I want to make millions of `socket.Read()` calls" remains awkward.

## What's been considered

- **Coroutines / fibers** — not coming; the runtime team has consistently said "we have async, we'll evolve it instead".
- **Task pooling and reduced allocation** — *is* coming and has been (`PoolingAsyncValueTaskMethodBuilder`, IValueTaskSource).
- **Hot async machinery improvements** — every release reduces allocation and overhead. .NET 9 made async stack traces clearer and reduced state-machine size.
- **Structured concurrency primitives** — proposed (`StructuredTaskScope`-like). May arrive in a future release.

## What you should do today

- **Use `async`/`await` for IO**. Treat it like virtual threading.
- **Use `Channel<T>` and actors** for state. Treat them like Erlang-style processes.
- **Don't wait for green threads in .NET**. They're unlikely to arrive in the Loom shape.

## Predictions for .NET 11+

(This is speculation, dated 2026.)

- `Task.WhenEach`, `ConfigureAwaitOptions`, pooled builders: continued evolution along the existing axes.
- More `Span`-friendly async APIs.
- Possibly a built-in `TaskScope`/`TaskGroup` for structured concurrency.
- Continued IO improvements via `io_uring` on Linux.
- More aggressive auto-pooling / auto-`ValueTask` conversions in the compiler.

There's no announced plan to make `async` invisible. The model that exists today is what you'll be writing in 2030.
