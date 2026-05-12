# Where false sharing hides in real code

False sharing is a layout bug, and layout is invisible in the source. Two reviewers can stare at code and not notice the problem; only the *machine layout* exposes it. This file is a catalogue of the patterns where it most commonly hides, with the why and a sketch of the fix.

## Pattern 1 — per-thread counters in an array

The textbook trap:

```csharp
long[] counters = new long[Environment.ProcessorCount];

Parallel.For(0, items.Length, i =>
{
    int slot = Thread.GetCurrentProcessorId() % counters.Length;
    counters[slot]++;
});
```

A `long` is 8 bytes. Eight longs fit in one 64-byte cache line. Threads 0–7 all hit the same line. Every write invalidates every other writer's copy. Maximum coherence pain.

**Fix** — space the counters out:

```csharp
const int CountersPerLine = 8;
const int Spacing = 16;   // pad to 128 bytes per counter (covers 128 B line, gives room)
long[] counters = new long[Environment.ProcessorCount * Spacing];

Parallel.For(0, items.Length, i =>
{
    int slot = (Thread.GetCurrentProcessorId() % Environment.ProcessorCount) * Spacing;
    counters[slot]++;
});

long total = 0;
for (int p = 0; p < counters.Length; p += Spacing) total += counters[p];
```

Or use `PaddedLong[]` (see [03-Fixing-It.md](03-Fixing-It.md)).

## Pattern 2 — hot fields adjacent in a class

```csharp
public sealed class Connection
{
    public long BytesSent;       // written by send thread
    public long BytesReceived;   // written by receive thread
}
```

On 64-bit, after the class header (~16 B on 64-bit CoreCLR), the next field starts at offset 16. `BytesSent` at 16, `BytesReceived` at 24. Both on the same line. Both written from different threads. False-shared.

**Fix** — pad explicitly:

```csharp
[StructLayout(LayoutKind.Explicit)]
public sealed class Connection
{
    [FieldOffset(0)]   public long BytesSent;
    [FieldOffset(128)] public long BytesReceived;
}
```

Or interpose padding fields:

```csharp
public sealed class Connection
{
    public long BytesSent;
    private long _pad0, _pad1, _pad2, _pad3, _pad4, _pad5, _pad6, _pad7;   // 64 B
    public long BytesReceived;
}
```

Less elegant; same effect.

## Pattern 3 — hand-rolled ring buffer / queue head and tail

```csharp
public sealed class RingBuffer<T>
{
    private long _head;     // written by consumer
    private long _tail;     // written by producer
    private T[] _items;
}
```

`_head` and `_tail` sit at adjacent offsets after the class header. Producer writes tail; consumer writes head. Both on the same line, all the time. This is one of the most performance-destroying patterns in single-producer-single-consumer queues — and one of the most common hand-rolled mistakes.

**Fix** — pad between them:

```csharp
public sealed class RingBuffer<T>
{
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    struct PaddedLong { [FieldOffset(0)] public long Value; }

    private PaddedLong _head;
    private PaddedLong _tail;
    private T[] _items;
}
```

The .NET **BCL** (Base Class Library — the `System.*` types in the runtime) has `ConcurrentQueue<T>` using similar tricks internally — its `Segment` class has padding around the producer and consumer cursors. Look at the source on GitHub; the technique is exactly what you'd write yourself.

## Pattern 4 — struct array with hot byte fields

```csharp
struct WorkerState
{
    public long LastSeen;     // updated by the worker
    public int Status;        // read by the supervisor
    public int Errors;        // updated by the worker on error
}

WorkerState[] workers = new WorkerState[N];
```

Each `WorkerState` is 16 bytes (8 + 4 + 4). Four of them fit in 64 B; eight fit in 128 B. Adjacent workers' fields share lines. Supervisor reading `Status[5]` invalidates worker 6's `LastSeen` update — a classic read-write false sharing scenario.

**Fix** — pad each struct to a line, or change the access pattern (separate-arrays-per-field):

```csharp
// Option A: pad the struct
[StructLayout(LayoutKind.Explicit, Size = 128)]
struct WorkerState
{
    [FieldOffset(0)]  public long LastSeen;
    [FieldOffset(8)]  public int  Status;
    [FieldOffset(12)] public int  Errors;
}

// Option B: SoA (Structure of Arrays) — usually faster overall
long[] lastSeen = new long[N];
int[]  status   = new int[N];
int[]  errors   = new int[N];
// Adjacent indices in 'lastSeen' still false-share among writers,
// so the same padding rules apply.
```

## Pattern 5 — `static readonly` fields on a hot type

```csharp
public static class Stats
{
    public static long Hits;
    public static long Misses;
    public static long Errors;
}
```

Statics live on the type's static space — typically a single block, sequentially laid out. Three `long`s = 24 bytes; all on one line. If you have any threads touching different ones, they false-share.

**Fix** — same padding tricks, or use `Counter<long>` from `System.Diagnostics.Metrics` (which handles dimensionality and aggregation properly).

## Pattern 6 — `Random.Shared` was a write hotspot pre-.NET 6

`System.Random` before .NET 6 was thread-unsafe; a static shared instance caused both correctness bugs and contention. Modern `Random.Shared` is per-thread internally. Lesson: BCL types evolve to fix exactly this; check what version you're on before papering over with locks.

## Pattern 7 — generic locks adjacent in a list

```csharp
private static readonly object[] _stripes = new object[64];
```

The references themselves don't false-share (each `Monitor.Enter` updates a per-instance lock state, not the array slot). But if you index into the array via `stripes[hash & 63]` from many threads, the *array slots* may share lines — and if you're writing the slots (e.g., replacing locks lazily), you false-share.

**Fix** — initialise the array once, never write the slots; or use `PaddedReference[]`.

## Pattern 8 — accidentally-shared `lock` target

```csharp
class Cache
{
    private static readonly object _lock = new();
    public void Add(...) { lock (_lock) { ... } }
}
```

The lock object itself (`_lock`) lives on the heap. *Monitor* maintains state in the object header — the same cache line as `_lock`'s vtable. If you have *another* hot object with adjacent allocation order, they may share lines. Usually not a problem in practice but worth knowing when chasing a microbenchmark.

## How to spot these patterns

**Code-level smells:**

- Multiple adjacent fields in a class or struct of the same primitive type, each written by a different thread.
- An array of primitives indexed by `ProcessorId` or thread index without spacing.
- "Producer cursor + consumer cursor" patterns in hand-rolled queues.
- A `static` block with many counters.

**Measurement-level smells:**

- A multithreaded benchmark that scales sub-linearly *with no apparent contention*.
- High `cache-misses` per instruction on threads that should hit L1.
- A `perf c2c` (Linux) showing cross-core line transfers from your code.

## Practical takeaways

- It's a layout problem. The source code looks fine; the *bytes* are the bug.
- The same patterns repeat across codebases. Once you see one, you'll start seeing them everywhere.
- Padding is cheap. 120 wasted bytes per counter is irrelevant in 2026.
- BCL types you use already pad where they need to. Your hand-rolled equivalents probably don't.

## Lab

`FalseSharingDemo` (demo 1) is the canonical reproducer. Modify it to introduce *three* false-sharing variations:

1. Packed struct (existing): two longs adjacent.
2. Class with two adjacent `long` fields, written by two threads.
3. Array of longs at indices 0 and 1, written by two threads.

All three should show similar slowdowns; the fix is the same.

## Further reading

- **Martin Thompson — *Going Reactive*** posts on padding lock-free structures.
- **`ConcurrentQueue<T>` source** in the .NET runtime — see how the BCL pads internally.
- **Wikipedia — *False sharing*** — has worked examples with diagrams.
