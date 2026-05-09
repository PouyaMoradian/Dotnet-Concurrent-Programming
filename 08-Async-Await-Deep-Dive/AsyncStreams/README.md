# Async streams (`IAsyncEnumerable<T>`)

`IAsyncEnumerable<T>` + `await foreach` is the streaming async iteration model. It's the right tool for:

- **Paginated APIs.** Yield one page at a time, transparently.
- **Streaming reads** (lines from a network stream, rows from a database).
- **Anything where "give me the next item" is async**.

## Defining one

```csharp
public async IAsyncEnumerable<string> ReadLinesAsync(
    string path,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    using var fs = File.OpenRead(path);
    using var sr = new StreamReader(fs);
    string? line;
    while ((line = await sr.ReadLineAsync(ct)) is not null)
    {
        yield return line;
    }
}
```

Two compiler attributes you must know:

- `[EnumeratorCancellation]` — without this on the parameter, the cancellation token passed via `WithCancellation` won't reach the body. **Always add it.**
- `ConfigureAwait(false)` for libraries — applied per-await internally, but you may want `await foreach (var x in stream.ConfigureAwait(false))` at the call site too.

## Consuming with cancellation

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await foreach (var line in ReadLinesAsync(path).WithCancellation(cts.Token))
{
    ProcessLine(line);
    if (line.StartsWith("STOP")) break;
}
```

Breaking out of `await foreach` calls `DisposeAsync` on the enumerator, which cleans up the `using` blocks inside the producer. **Don't manually iterate** with `await enumerator.MoveNextAsync()` unless you're prepared to dispose.

## `WithCancellation` vs producer cancellation

The `WithCancellation(token)` is a separate token from any token captured at `IAsyncEnumerable<T>` creation. If your producer was given a token via constructor and the consumer passes another via `WithCancellation`, both tokens cancel the iteration (the framework links them). So consumers can shorten the lifetime; producers can shorten it for their own reasons.

## Backpressure

`IAsyncEnumerable<T>` is naturally pull-based: consumer calls `MoveNextAsync`, producer yields one item. The producer is paused between yields. **Backpressure is built in for free** — slow consumers slow producers automatically.

For push-based scenarios (events arriving regardless of consumer speed), use `Channel<T>` with a bounded capacity so the producer is forced to wait. Don't try to fake it with `IAsyncEnumerable<T>`.

## Combining

LINQ for `IAsyncEnumerable<T>` lives in `System.Linq.Async` (NuGet) or `System.Linq` extensions in .NET 9+ for some operators. Common ones:

```csharp
var ids = items.Where(x => x.Active).Select(x => x.Id);          // IAsyncEnumerable<int>
var batched = items.Buffer(100);                                  // IAsyncEnumerable<IList<T>>
var first10 = await items.Take(10).ToListAsync();
```

## Performance notes

- `IAsyncEnumerable<T>` has more per-item overhead than a tight `for` loop. For very small items (< 100 ns of work), the iteration cost is significant. Batch them (`Buffer(N)`) for hot paths.
- The compiler-generated state machine for `async IAsyncEnumerable<T>` allocates one box per call. Reuse the enumerable when possible; don't re-create per iteration.
