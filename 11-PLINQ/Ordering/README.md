# Ordering

By default, PLINQ does **not** preserve source order. Workers run in parallel; results emerge in any order. To preserve order, call `.AsOrdered()`:

```csharp
source.AsParallel()
      .AsOrdered()                       // preserve source order in outputs
      .Where(IsInteresting)
      .Take(100)                          // first-100 by source order, not by time
      .ToArray();
```

## What ordering costs

The merger must buffer: a partition's output that finished early is held until earlier partitions finish. Two effects:

- **Throughput** drops 10–30% in typical chains.
- **Latency** to first item may grow significantly.

If your downstream doesn't need order, `.AsUnordered()` undoes any earlier `AsOrdered()`. (`AsParallel()` of a `Range` is implicitly ordered; `.AsUnordered()` after that is the fast path.)

## Order-sensitive operators

Some operators imply order:

| Operator | Sensitive |
|---|---|
| `Take` / `Skip` / `TakeWhile` / `SkipWhile` | Yes |
| `First` / `Last` | Yes |
| `ElementAt` | Yes |
| `OrderBy` / `OrderByDescending` | They impose their own |
| `Where` / `Select` / `Sum` / `Count` | No |

Composing `.AsOrdered().OrderBy(...)` is wasteful — the second `OrderBy` re-sorts; the first `AsOrdered` was preserving an order you immediately abandon.

## Practical advice

- **Default to `.AsUnordered()` if the source is `Range` or `Enumerable.Range`** but order doesn't matter — squeeze the extra throughput.
- **Use `AsOrdered` for `Take`/`First` semantics** that *must* respect source order.
- **Don't use PLINQ for "fastest first" streaming** — use `Task.WhenEach` (.NET 9) or `IAsyncEnumerable<T>` instead.
