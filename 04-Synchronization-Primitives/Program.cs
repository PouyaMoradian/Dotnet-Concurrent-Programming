using Chapter04.Demos;
using Concurrency.Shared;

await ConsoleLab.Run("Chapter 04 — Synchronization Primitives",
[
    ("lock vs Interlocked vs Volatile (counters)", LockVsInterlocked.Run),
    ("SemaphoreSlim — async concurrency cap",      SemaphoreSlimDemo.Run),
    ("ReaderWriterLockSlim — readers vs writers",  RwLockDemo.Run),
    ("Barrier — phased computation",               BarrierDemo.Run),
    ("Async-friendly lock (SemaphoreSlim(1,1))",   AsyncLockDemo.Run),
],
args);
