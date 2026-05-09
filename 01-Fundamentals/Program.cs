using Chapter01.Demos;
using Concurrency.Shared;

await ConsoleLab.Run("Chapter 01 — Fundamentals",
[
    ("Process vs Thread (cost)",        ProcessVsThreadDemo.Run),
    ("Concurrency without parallelism", ConcurrencyVsParallelismDemo.Run),
    ("Sync vs Async (thread cost)",     SyncVsAsyncDemo.Run),
    ("Mutable state — race demo",       MutableStateRace.Run),
    ("Memory visibility — torn read",   MemoryVisibilityDemo.Run),
    ("Work stealing — local vs global", WorkStealingDemo.Run),
],
args);
