# State and Mutability

> Concurrency bugs are bugs of *state*. There is no race on data that nobody mutates.

This is the single highest-leverage idea in concurrent programming, and it applies independently of language, runtime, or platform.

## The ladder of safety

From safest to most dangerous:

1. **No state.** Pure functions. Trivially safe to call from any number of threads.
2. **Immutable state.** `record`, `ImmutableArray<T>`, `FrozenDictionary<K,V>`. Once published, threads can read freely.
3. **Confined state.** State held by one and only one thread (`ThreadLocal<T>`, actor mailboxes, channel readers).
4. **Single-writer state.** Many readers, exactly one writer. Often solvable with `Volatile.Read/Write` (preview of [05-Atomic-Operations](../../05-Atomic-Operations/)).
5. **Synchronised mutable state.** Many readers and writers, protected by locks/atomics. The hard case.
6. **Unsynchronised mutable state.** A bug.

Climb the ladder as far as your design allows before paying for synchronisation primitives.

## Practical .NET patterns

### Immutable types — the cheap win

```csharp
// 1. Records — value-based, with-expressions, easy.
public record Configuration(string Endpoint, int MaxRetries);

// 2. Immutable collections — share by reference, mutate by copy.
ImmutableArray<int> primes = [2, 3, 5, 7];
ImmutableArray<int> withEleven = primes.Add(11);  // primes unchanged

// 3. Frozen collections (.NET 8) — built once, then optimised for read.
var byId = users.ToFrozenDictionary(u => u.Id);
```

### Copy-on-write for hot configuration

A common production pattern: a worker reads a config object on every iteration. Updates are rare (manual or via a watcher).

```csharp
public sealed class HotConfig
{
    private static Configuration _current = new("https://default", 3);

    public static Configuration Current => Volatile.Read(ref _current!);

    public static void Update(Configuration next) => Volatile.Write(ref _current!, next);
}
```

The reads are lock-free; writes are atomic publication. No reader ever sees a half-built object because reference assignment of a properly-aligned reference is atomic on 64-bit (per ECMA-335 §I.12.6.6).

### Confinement via channels / actors

If state is owned by one task that consumes a `Channel<Command>`, no synchronisation primitive is needed inside that task. This is the actor model, in 5 lines of C# — and it's how you solve "shared mutable state" by removing the *shared* part. See [09-Channels/ActorPatterns](../../09-Channels/ActorPatterns).

## The interview question

> "What's the difference between thread safety and immutability?"

Immutability is a *property of the type*. Thread safety is a *property of how the type is used*. An immutable type is thread-safe trivially. A mutable type can be thread-safe (`ConcurrentDictionary`) or not (`Dictionary`). Most production bugs are mutable types being used as if they were thread-safe.
