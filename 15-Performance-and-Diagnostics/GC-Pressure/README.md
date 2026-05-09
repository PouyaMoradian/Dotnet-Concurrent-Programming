# GC pressure

The .NET garbage collector is fast, but it's not free. Allocation-heavy code spends time *in* GC instead of doing work. Worse, GC pauses (especially Gen 2) cause latency spikes that bypass any tail-latency mitigation you've built.

## Spotting GC pressure

Live (`dotnet-counters`):

| Counter | Healthy | Suspicious |
|---|---|---|
| `time-in-gc` | < 5% | > 15% |
| `gen-0-gc-count` per minute | tens | thousands |
| `gen-2-gc-count` per minute | < 1 | > 10 |
| `alloc-rate` | < 100 MB/s | > 500 MB/s sustained |
| `loh-size`, `poh-size` | bounded | growing |

Trace-level (`dotnet-trace --providers Microsoft-Windows-DotNETRuntime:0x1:5`):

- Each `GCStart_V2` / `GCEnd_V1` pair tells you the duration. Histogram them; look for outliers.

## Reducing pressure

### 1. Span and stackalloc

```csharp
// Allocates on the heap
string Hex(int x) => Convert.ToString(x, 16);

// Span-based, stack-allocated buffer
public static int Hex(Span<char> buffer, int x) { /* ... */ }
```

For interim buffers (parsing, formatting), `stackalloc Span<byte>` keeps the buffer on the stack. No heap, no GC.

### 2. ArrayPool / MemoryPool

Reuse large arrays instead of allocating per call:

```csharp
var buf = ArrayPool<byte>.Shared.Rent(8192);
try { /* use buf[0..8192] */ }
finally { ArrayPool<byte>.Shared.Return(buf); }
```

For high-frequency code paths, this turns "thousands of GC's worth of arrays" into "one shared pool".

### 3. String avoidance

- **`StringBuilder` reuse** with `[ThreadStatic]`.
- **`string.Create<TState>(int, TState, SpanAction<char, TState>)`** for allocation-free formatting.
- **`Utf8Formatter` / `Utf8Parser`** for UTF-8 paths that avoid the UTF-16 round trip.

### 4. ValueTask + pooled state machines

See [08/AllocationFreeAsync](../../08-Async-Await-Deep-Dive/AllocationFreeAsync/). Async hot paths are major allocators.

### 5. Avoid box/unbox

`object`-typed APIs box value types. Watch out for:

- `Tuple<int, int>` (allocates) vs `(int, int)` (does not).
- `IEnumerable<int>` boxing each element when assigning from a struct enumerator.
- `string.Format` (boxes args) vs interpolation (also boxes; .NET 6+ improves with interpolation handlers).

## GC modes — pick the right one

| Mode | Default for | Latency | Throughput |
|---|---|---|---|
| Workstation, non-concurrent | desktop apps (older default) | high pause | low |
| Workstation, concurrent | desktop apps | medium pause | medium |
| **Server, concurrent** | **server apps (default)** | **low pause** | **high** |
| Server, non-concurrent | unusual | very low pause | very high |

This repo's `Directory.Build.props` sets server + concurrent, which is the right choice for almost every multicore process.

## LOH (Large Object Heap)

Allocations ≥ 85,000 bytes go on the LOH. The LOH is collected with Gen 2 → expensive. Tactics:

- **Avoid LOH allocations on hot paths.** Slice large operations.
- **`GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce`** schedules a one-time compaction. Use sparingly — pause cost.

## POH (Pinned Object Heap, .NET 5+)

A separate heap for pinned objects (network buffers, P/Invoke targets). Avoids fragmenting the regular heap. Used internally by `GC.AllocateUninitializedArray<T>(length, pinned: true)`.

## Background GC

Enabled by default. Gen 0 / 1 happen as STW (stop-the-world) at thread safepoints — typically < 10 ms. Gen 2 starts STW briefly, then runs concurrently with the program. Background GC is a major reason latency P99s are tolerable.

## When to worry

If your service has visible latency spikes with no other explanation, capture a trace, look at GC durations. If Gen 2 events take > 50 ms, you have either:

- LOH activity (large arrays).
- Compaction happening.
- Pinning in the regular heap.

Each has its fix; no general advice.
