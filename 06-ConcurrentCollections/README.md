# 06 — Concurrent Collections

> **Layer:** BCL
> **Reading time:** ~25 minutes
> **Prereq:** [04](../04-Synchronization-Primitives/), [05](../05-Atomic-Operations/)

`System.Collections.Concurrent` plus `System.Collections.Immutable` and `System.Collections.Frozen` are the right answer to most "I need a thread-safe X" questions. This chapter is about picking the right one and using it well.

## The map

| Type | Order | Mutation | Best when |
|---|---|---|---|
| `ConcurrentDictionary<K,V>` | none | yes | Read-heavy or mixed dictionary |
| `ConcurrentQueue<T>` | FIFO | yes | Producer/consumer FIFO |
| `ConcurrentStack<T>` | LIFO | yes | Pool-like reuse, work-stealing |
| `ConcurrentBag<T>` | none (per-thread) | yes | Many producers/many consumers, no order |
| `BlockingCollection<T>` | adaptable | yes | Simple bounded producer/consumer (legacy — prefer `Channel<T>`) |
| `ImmutableArray<T>` / `ImmutableList<T>` / `ImmutableDictionary<K,V>` | varies | copy-on-write | Read-heavy, occasional updates |
| `FrozenDictionary<K,V>` / `FrozenSet<T>` (.NET 8) | none | none after build | Build-once, read-many |

## In-chapter folders

| Folder | Topic |
|---|---|
| [ConcurrentDictionary](ConcurrentDictionary/) | Striped locks, `GetOrAdd` semantics, value-factory race |
| [ConcurrentQueue](ConcurrentQueue/) | Segmented lock-free queue internals |
| [ConcurrentBag](ConcurrentBag/) | Per-thread queues + stealing; for what *exactly*? |
| [BlockingCollection](BlockingCollection/) | Wraps the others with bounded blocking semantics; mostly superseded |
| [ImmutableCollections](ImmutableCollections/) | Persistent data structures, structural sharing, performance |

## Patterns to memorise

### Idempotent cache (`GetOrAdd`)

```csharp
private readonly ConcurrentDictionary<string, Lazy<HttpClient>> _clients = new();

public HttpClient ClientFor(string baseUrl) =>
    _clients.GetOrAdd(baseUrl,
        url => new Lazy<HttpClient>(() => new HttpClient { BaseAddress = new(url) },
                                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;
```

The wrapping `Lazy<T>` matters: `ConcurrentDictionary.GetOrAdd` may invoke its value factory **multiple times** under contention (only one wins; others are discarded). If creation is expensive or has side effects, wrap in `Lazy<T>`. The race becomes harmless: at most one `HttpClient` is constructed.

### Producer/consumer

```csharp
// Old: BlockingCollection<T>
var queue = new BlockingCollection<int>(boundedCapacity: 100);
// producer:
foreach (var x in source) queue.Add(x);
queue.CompleteAdding();
// consumer:
foreach (var x in queue.GetConsumingEnumerable()) Process(x);

// New (preferred): Channel<T> — see Chapter 9.
```

`BlockingCollection<T>` is correct but synchronous-only. For modern code use `Channel<T>` (`Channel.CreateBounded`, `Channel.CreateUnbounded`) — async, allocation-friendly, faster.

## Run

```bash
dotnet run --project 06-ConcurrentCollections
```
