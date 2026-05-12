# Fixing false sharing

The fix is always *separate the contended fields onto different cache lines*. Below are the practical techniques, ranked by readability and performance, with their trade-offs.

## Technique 1 — `StructLayout(LayoutKind.Explicit, Size = 128)`

The cleanest expression of "this value gets its own cache line":

```csharp
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Explicit, Size = 128)]
public struct PaddedLong
{
    [FieldOffset(0)] public long Value;
}
```

Use it as a field type:

```csharp
public sealed class HotCounters
{
    public PaddedLong Hits;
    public PaddedLong Misses;
    public PaddedLong Errors;
}
```

Each `PaddedLong` consumes 128 bytes — enough to occupy an entire 64-B line (with margin) or one 128-B Apple Silicon line. Adjacent `PaddedLong` instances never share a cache line.

Pros:
- Intent is obvious at the call site.
- One declaration; reusable everywhere.

Cons:
- 128 bytes per `long` is ~16× the data cost. Don't slap it on every counter — only the contended ones.

For 32-bit primitives use the same technique with `int`/`uint`/`float` at offset 0:

```csharp
[StructLayout(LayoutKind.Explicit, Size = 128)]
public struct PaddedInt
{
    [FieldOffset(0)] public int Value;
}
```

## Technique 2 — explicit field offsets in a hot class

When the class has *several* hot fields all written from different threads, lay it out explicitly:

```csharp
[StructLayout(LayoutKind.Explicit)]
public sealed class ConnectionStats
{
    [FieldOffset(0)]   public long BytesSent;       // own line
    [FieldOffset(128)] public long BytesReceived;   // own line
    [FieldOffset(256)] public long ErrorsSent;      // own line
    [FieldOffset(384)] public long ErrorsReceived;  // own line
}
```

You waste ~480 bytes per instance, but if there are 10–100 of them in a long-lived service, the memory is irrelevant and the contention cost is large.

Caveat: `LayoutKind.Explicit` on reference types is allowed but rare. Confirm it compiles and that field accesses end up where you expect — the JIT respects the layout.

## Technique 3 — `[FieldOffset]` with explicit padding fields

A more verbose but legible alternative:

```csharp
public sealed class ConnectionStats
{
    public long BytesSent;
    private long _pad0, _pad1, _pad2, _pad3, _pad4, _pad5, _pad6, _pad7;   // 64 bytes
    private long _pad8, _pad9, _padA, _padB, _padC, _padD, _padE, _padF;   // another 64 bytes
    public long BytesReceived;
    // ...
}
```

This is what hand-rolled lock-free data structures in older codebases look like. It works, it's explicit, but it's noisy and easy to get wrong (forgetting a padding field on a refactor).

## Technique 4 — array of padded structs

```csharp
PaddedLong[] counters = new PaddedLong[Environment.ProcessorCount];

void Increment()
{
    int slot = Thread.GetCurrentProcessorId() % counters.Length;
    Interlocked.Increment(ref counters[slot].Value);
}
```

Because `PaddedLong` is 128 bytes, adjacent array elements are on different lines. This is the bread-and-butter pattern for sharded counters.

## Technique 5 — `ThreadLocal<T>`

When you don't care about exact CPU affinity, only "per-thread state":

```csharp
private static readonly ThreadLocal<long> _local = new(() => 0L, trackAllValues: true);

void Increment() => _local.Value++;
long Total => _local.Values.Sum();
```

`ThreadLocal<T>` allocates a wrapper per thread; each thread's wrapper is on a different heap allocation, so cache lines never collide. No explicit padding needed.

Trade-off: `ThreadLocal` has slightly higher overhead per increment than an indexed `PaddedLong[]` (the lookup is more complex). For very hot counters where the overhead matters, sharded arrays win; for normal use, `ThreadLocal` is cleaner.

## Technique 6 — `Parallel.For` with `localInit`/`localFinally`

The cleanest approach when the work is naturally parallel:

```csharp
long total = 0;
Parallel.For(0, items.Length,
    localInit: () => 0L,
    body: (i, _, local) => local + Process(items[i]),
    localFinally: local => Interlocked.Add(ref total, local));
```

Each worker keeps a private `local` (a register/L1 value). No false-sharing possible — there are no shared writes inside the loop. Only the final `Interlocked.Add` per worker touches the shared total, once per worker.

## Technique 7 — Restructure to avoid the shared write entirely

The best fix is sometimes "don't share". Two patterns:

- **Compute locally, publish at the end.** Each task builds its result in isolation; combine at the end with `Aggregate`/sum/concatenation.
- **Use immutable read-only state on the read side.** A reader that never writes never contributes to false sharing.

These are good design defaults even without false sharing in mind.

## What the BCL (Base Class Library) does internally

The .NET runtime uses padded types internally where it matters:

- **`ConcurrentQueue<T>.Segment`** has a padded `_headAndTail` struct.
- **`ConcurrentDictionary<TKey, TValue>`** stripes its locks and pads the lock array.
- **`Random.Shared`** uses per-thread instances internally.
- **`Counter<long>` (System.Diagnostics.Metrics)** uses sharded accumulation; the implementation is in `Sdk.cs` / `MetricsManager.cs`.

The `[StructLayout(LayoutKind.Explicit, Size = 192)]` you might see in BCL source is exactly the technique above, sized to a comfortably-large multiple of typical line sizes. Some internal types use a `PaddingFor32` field that adds 32 bytes — this is for *48-byte-line* targets that don't exist on real hardware but are kept for portability assumptions.

## A reference table

| Scenario | Recommended technique |
|---|---|
| Per-thread counter array | `PaddedLong[]` indexed by `ProcessorId` |
| Two hot fields in a class | `[StructLayout(Explicit)]` with offsets |
| Per-task accumulation in a `Parallel.For` | `localInit`/`localFinally` |
| Single hot atomic in long-running code | `ThreadLocal<long>` + `Sum` on read |
| Counter with high tag cardinality | `System.Diagnostics.Metrics.Counter<T>` |
| Hand-rolled **SPSC** queue (Single-Producer Single-Consumer) | `PaddedLong` for `_head` and `_tail` |

## Anti-patterns

### ❌ "I'll just pad with a comment"
```csharp
public long Value;
// IMPORTANT: keep 8 longs of padding here for cache line alignment
public long OtherValue;
```
The comment isn't checked by anything. The next refactor will break it.

### ❌ Padding once, not on the second writer
```csharp
[StructLayout(LayoutKind.Explicit, Size = 64)]
struct OneLineLong { [FieldOffset(0)] public long V; }

OneLineLong _a;     // own line
long _b;            // <-- may live on the *next* line, but if anything follows
                    //     it on the same line and is written, you get
                    //     false sharing with that.
```
Pad *both* sides of the boundary. The size-of-64 trick gives the *padded* struct its own line, but anything adjacent to it can still false-share with whatever follows.

### ❌ Trusting struct layout in arrays without verifying

`new PaddedLong[N]` is fine — array elements are sequential by `sizeof(struct)`. But `new MyClassWithPaddedField[N]` has *references*, and the *referenced objects* may or may not be cache-line-aligned. For collections of padded data, prefer arrays of structs.

## Practical takeaways

- Reach for `[StructLayout(LayoutKind.Explicit, Size = 128)]` first — it's the cleanest expression.
- For per-thread state, `ThreadLocal<T>` or `Parallel.For`'s `localInit` are even cleaner.
- Pad to 128 bytes, not 64 — covers Apple Silicon and large-line POWER systems too.
- Measure before and after. A 5–10× speedup confirms you found the bug; a flat number means false sharing wasn't the problem.

## Lab

`FalseSharingDemo` already demonstrates technique 1. Add a variant that uses `ThreadLocal<long>` and time it — it should match or beat the padded version, and is dramatically more readable.

## Further reading

- **Stephen Cleary — *Padding fields*** posts on the .NET blog.
- **Joe Duffy — *Concurrent Programming on Windows*** — chapter 7's examples are still the best worked references.
- **`PaddingHelpers` source** in `dotnet/runtime` for the internal pattern names.
