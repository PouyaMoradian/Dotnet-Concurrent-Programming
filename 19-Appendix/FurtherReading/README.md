# Further reading

## Books

- **Joe Duffy — *Concurrent Programming on Windows*** (2008). The single most thorough .NET-centric concurrency book ever written. Pre-`async/await` but the foundations are unchanged.
- **Maurice Herlihy & Nir Shavit — *The Art of Multiprocessor Programming***. The academic foundation. Treiber stacks, Michael-Scott queues, hazard pointers.
- **Paul McKenney — *Is Parallel Programming Hard, And, If So, What Can You Do About It?*** Free PDF; excellent on memory models and lock-free algorithms.
- **Stephen Cleary — *Concurrency in C# Cookbook***. Practical, modern, opinionated.
- **Brendan Gregg — *Systems Performance*** (2nd ed). For the system view: scheduler, IO stack, observability tools.

## Microsoft devblogs and Stephen Toub

Stephen Toub's posts on devblogs.microsoft.com are essential reading for any .NET concurrency practitioner. Key posts (search by title):

- "How async/await really works in C#"
- "ValueTask: returning Tasks asynchronously"
- "An Introduction to System.Threading.Channels"
- "Performance Improvements in .NET <each version>"
- "ConfigureAwait FAQ"
- "Async ValueTask Pooling in .NET 5"

The "Performance Improvements in .NET" series tells you, release by release, what the runtime team did to your concurrency primitives.

## Papers

See [AcademicPapers](../AcademicPapers/) for the canonical concurrency papers.

## Talks

- **Mads Torgersen — "C# Async Internals"** (any year — they iterate on it).
- **Stephen Toub — "Performance & Diagnostics"** at .NET Conf.
- **Vance Morrison — "What every developer must know about multithreaded apps"** (still gold).
- **Jeff Preshing — "Acquire and Release Semantics"** (his blog series; the talk is at Cppcon but applies to .NET memory model).

## Blogs

- **Jeff Preshing — preshing.com**: hands-down the best programmer-friendly memory-model resource.
- **Marc Gravell — marcgravell.com**: hot-path .NET, allocations, async patterns.
- **Brad Bouchard / hibernating rhinos**: lock-free patterns in .NET libraries.
- **Adam Sitnik — adamsitnik.com**: BenchmarkDotNet, span, hot perf.

## .NET runtime source

For the truly curious: the `dotnet/runtime` repo on GitHub contains the actual implementations of `ThreadPool`, `Task`, `Channel<T>`, `ConcurrentDictionary`, etc. They are well-commented and surprisingly readable. Start with `src/libraries/System.Private.CoreLib/src/System/Threading/`.

## The classics, brief mentions

- *The Mythical Man-Month* — Brooks. Concurrency in the project-management sense.
- *Designing Data-Intensive Applications* — Kleppmann. Distributed concurrency primitives.
- *Operating Systems: Three Easy Pieces* — Arpaci-Dusseau. Free; OS-level concurrency.
