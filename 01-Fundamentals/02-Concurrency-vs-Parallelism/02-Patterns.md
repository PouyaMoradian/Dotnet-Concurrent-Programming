# Patterns — where each property shows up

The distinction between concurrency and parallelism isn't a parlour trick. Each one comes with its own *design patterns*, and most concurrency bugs in production are someone using a parallelism pattern for a concurrency problem or vice versa.

## Concurrency patterns

These are about **structure** — organising independent activities so they cooperate without colliding.

### Request handler

The web server pattern. Each incoming request is an independent activity; the runtime juggles many of them at once. Each request typically awaits IO (database, downstream service) and the runtime uses those gaps to advance other requests.

```csharp
public async Task<IResult> Handler(int userId)
{
    var user = await _db.GetUser(userId);
    var prefs = await _prefs.GetPreferences(userId);
    return Results.Ok(new { user, prefs });
}
```

Concurrency: many requests in flight. Each individual request is sequential; the *server* is concurrent.

### Producer / consumer

One or more producers put items into a bounded buffer; one or more consumers take them out. Classic shape; in .NET prefer `Channel<T>` over `BlockingCollection<T>` for async-friendly back-pressure.

```csharp
var channel = Channel.CreateBounded<Work>(capacity: 100);

_ = Task.Run(async () =>
{
    await foreach (var w in channel.Reader.ReadAllAsync(ct))
        await ProcessAsync(w);
});

await channel.Writer.WriteAsync(new Work(...));
```

Concurrency: producer and consumer run independently, exchanging work through a queue. Parallelism is incidental; you scale by adding more consumers.

### Pipeline

A sequence of stages, each running concurrently, each stage feeding the next via a channel. Throughput is bounded by the slowest stage (Amdahl's first cousin). See [10-TPL-Dataflow](../../10-TPL-Dataflow/) for the heavy version.

```
[ ingest ] → [ parse ] → [ validate ] → [ persist ]
```

### Actor / state confinement

One task owns a piece of state and exposes it only via messages on a channel. The state is mutated only by that one task, so no other synchronisation is needed. See [09-Channels/ActorPatterns](../../09-Channels/ActorPatterns).

### Fan-out / fan-in

Issue *n* concurrent sub-queries, wait for all (or some), aggregate.

```csharp
var tasks = ids.Select(id => _http.GetAsync($"/items/{id}"));
var results = await Task.WhenAll(tasks);
```

Concurrent over IO. Parallel is incidental — if the network is the bottleneck, the work isn't on the CPU at all.

## Parallelism patterns

These are about **execution** — getting more work done per unit time by using more cores.

### Data-parallel (map)

Apply the same function to every element of a collection independently. `Parallel.For`, `Parallel.ForEach`, PLINQ's `Select`.

```csharp
Parallel.For(0, image.Pixels.Length, i =>
{
    image.Pixels[i] = ApplyFilter(image.Pixels[i]);
});
```

The work per element should be substantial — at least a few microseconds — or the loop overhead dominates.

### Fork-join

Split a problem into independent subproblems, solve them in parallel, combine the results. The TPL's `Task.WhenAll` over `Task.Run`s is the basic form; `Parallel.Invoke` is the convenient one.

```csharp
Parallel.Invoke(
    () => SortAscending(left),
    () => SortAscending(right));
Merge(left, right);
```

The classic example is parallel merge sort.

### Map-reduce / aggregation

Apply a function to each element, then reduce the results with an associative operator. PLINQ's `Aggregate`, the `localInit`/`localFinally` overload of `Parallel.For`.

```csharp
long total = source.AsParallel()
                   .Where(x => x.IsValid)
                   .Sum(x => x.Amount);
```

`Sum` is associative, so PLINQ can compute per-partition sums in parallel and combine them with no synchronisation in the hot path.

### SIMD / vectorisation

Parallelism *within a single thread*, via wide registers (`Vector256<T>` = 8 floats at once on AVX2). The hardware does multiple operations per instruction. Doesn't need `async`, `Task`, or any threading — and it composes with the patterns above (each thread in a `Parallel.For` can use SIMD internally).

## When patterns mix — IO + CPU

Most real systems mix both. A web request fans out three sub-queries (concurrency), then runs an expensive aggregation on the results (parallelism), then awaits the response write (concurrency again).

```csharp
public async Task<IResult> Aggregate(int id)
{
    // Fan-out concurrent IO. (There is no built-in tuple-returning WhenAll;
    // start the tasks, await them together, then read the results. A tuple-returning
    // overload is available via the TaskTupleAwaiter NuGet package if you prefer.)
    var ta = _serviceA.Get(id);
    var tb = _serviceB.Get(id);
    var tc = _serviceC.Get(id);
    await Task.WhenAll(ta, tb, tc);
    var (a, b, c) = (ta.Result, tb.Result, tc.Result);

    // Parallel compute
    var merged = await Task.Run(() => MergeAndScore(a, b, c));

    // Final IO
    await _cache.Set(id, merged);
    return Results.Ok(merged);
}
```

This is the typical shape of a production handler. Notice the `Task.Run` around `MergeAndScore`: that's how you opt into a *parallel* pattern from within an *async* (concurrency) call site. Don't `Task.Run(async () => await ...)` — that's almost always wrong (see [18-Pitfalls](../../18-Pitfalls-and-Anti-Patterns/)).

## Anti-patterns to recognise

| Smell | What it usually means |
|---|---|
| `Parallel.ForEach(items, async i => await ...)` | Concurrency dressed as parallelism. Use `Parallel.ForEachAsync` instead. |
| `Task.Run(() => await SomethingAsync())` inside an `async` method | Pointless wrap; you're already on the pool. |
| `async` method that internally `Thread.Sleep`s | Parking the worker. Use `Task.Delay`. |
| `await Task.WhenAll(items.Select(i => Task.Run(() => Sync(i))))` for IO | Wraps sync IO in a pool thread per item. Make the underlying call async. |
| `Parallel.For` with a body that takes 100 ns | Loop overhead > work. Solve a bigger chunk per iteration or drop the parallelism. |

Each of these is a tell that the author hasn't distinguished "I want this to be concurrent" from "I want this to be parallel".
