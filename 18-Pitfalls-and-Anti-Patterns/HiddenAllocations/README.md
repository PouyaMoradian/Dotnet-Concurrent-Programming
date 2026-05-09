# Hidden allocations

C# code looks allocation-free until you look at the IL. Some patterns allocate where you don't expect, and on hot paths the allocator becomes the bottleneck.

## The usual suspects

### 1. LINQ in hot paths

```csharp
// Allocates an enumerator + closure per call
var sum = items.Where(x => x.IsActive).Sum(x => x.Value);
```

Every operator allocates state. For 1M iterations that's significant. Use a `for` loop in hot paths:

```csharp
var sum = 0L;
for (var i = 0; i < items.Length; i++)
    if (items[i].IsActive) sum += items[i].Value;
```

`List<T>` indexing is fast; `IEnumerable<T>.GetEnumerator()` may not be.

### 2. Closures capturing locals

```csharp
foreach (var x in items) Action(() => Use(x));    // closes over x
```

Each iteration allocates a fresh closure object plus the delegate. Pass `x` as state:

```csharp
foreach (var x in items) ActionWithState(static (state) => Use(state), x);
```

The `static` lambda has no closure. Modern BCL APIs (`ThreadPool.UnsafeQueueUserWorkItem`, `Task.Run<T>(state, ...)` patterns) accept state for this reason.

### 3. String interpolation with non-`ReadOnlySpan<char>` formatters

```csharp
log.LogInformation($"processing {item}");        // boxes; allocates
```

`Microsoft.Extensions.Logging` has its own template-based API:

```csharp
log.LogInformation("processing {Item}", item);   // structured; can be lazy
```

C# 10's interpolation handlers fix some of this for `LoggerMessageAttribute`-generated methods.

### 4. Boxing on `object`-typed APIs

```csharp
ConcurrentDictionary<int, object> dict = …;
dict[1] = 42;        // boxes 42 into an Int32 reference
```

If you want primitive values in a concurrent dictionary, use the typed generic: `ConcurrentDictionary<int, int>`.

### 5. Enumerator allocation on struct enumerators

```csharp
foreach (var x in someStructEnumerable) { … }
// vs.
foreach (var x in (IEnumerable<T>)someStructEnumerable) { … }   // ❌ boxes the struct enumerator
```

Some BCL types have struct enumerators (`Dictionary<K,V>`, `List<T>`). Casting to `IEnumerable<T>` boxes — which is why `IEnumerable<T>` LINQ chains over them allocate.

### 6. `Tuple<...>` vs ValueTuple

```csharp
var t = new Tuple<int, int>(1, 2);   // class — heap
var v = (1, 2);                       // ValueTuple — stack
```

Always prefer ValueTuple syntax for ad-hoc returns.

### 7. Async state machines

Each `async Task` method that suspends allocates the state machine box, the Task, and possibly the action delegate. See [08/AllocationFreeAsync](../../08-Async-Await-Deep-Dive/AllocationFreeAsync/).

### 8. Exceptions as control flow

`throw` is expensive — it walks the stack to gather the trace. `try/catch` is fine; *throwing* is the cost. Don't use exceptions for normal control flow.

## Detecting

- BenchmarkDotNet's `[MemoryDiagnoser]` — best for microbenchmarks.
- `dotnet-counters monitor System.Runtime --counters alloc-rate` — for live processes.
- `dotnet-trace` with `0x1` keyword (GC events) → PerfView's "GC Heap Net Mem" view shows top allocators.
- IL inspection with `ildasm` / sharplab.io.

## When to care

- **Hot paths** > 1M calls/sec: even a ~100-byte allocation per call is 100 MB/sec — major GC pressure.
- **GC-sensitive workloads** (low-latency, ML, image processing).
- **Allocation-bound benchmarks**: identify the allocator before optimising.

For typical line-of-business code, allocations rarely matter. **Profile before optimising.**
