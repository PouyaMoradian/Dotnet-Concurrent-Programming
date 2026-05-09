using Chapter08.Demos;
using Concurrency.Shared;

await ConsoleLab.Run("Chapter 08 — Async/Await Deep Dive",
[
    ("State machine — observe thread hops",   StateMachineDemo.Run),
    ("AsyncLocal vs ThreadStatic across awaits", AsyncLocalDemo.Run),
    ("ConfigureAwait(false) on a console host", ConfigureAwaitDemo.Run),
    ("IAsyncEnumerable<T> streaming",         AsyncStreamDemo.Run),
    ("IAsyncDisposable usage",                AsyncDisposableDemo.Run),
    ("ValueTask cache fast path",             ValueTaskFastPath.Run),
],
args);
