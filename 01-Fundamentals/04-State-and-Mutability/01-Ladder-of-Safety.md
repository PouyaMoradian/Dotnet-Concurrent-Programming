# The ladder of safety

Six rungs, from safest at the top to most dangerous at the bottom. Climb the ladder as far as your design allows before paying for synchronisation primitives.

## Rung 1 — No state

Pure functions. Trivially safe to call from any number of threads.

```csharp
public static int Add(int a, int b) => a + b;

public static double Distance(Point p1, Point p2) =>
    Math.Sqrt(Sq(p2.X - p1.X) + Sq(p2.Y - p1.Y));
```

Any thread, any time, any number of callers. No race possible because there's nothing to race on.

## Rung 2 — Immutable state

Once constructed, never mutated. Many threads can read freely; no thread can mutate, so there's no race.

```csharp
public record Configuration(string Endpoint, int MaxRetries);

var cfg = new Configuration("https://api.example.com", 3);
// Pass cfg to as many threads as you like. Safe.
```

For `record` and tuple types, "once constructed" means "after the constructor returns." For mutable types like arrays, you must enforce the immutability discipline yourself — see the next page.

## Rung 3 — Confined state

State held by one and only one thread. The state is mutable, but only one thread can see it. Examples:

- `ThreadLocal<T>` — each thread gets its own instance.
- `[ThreadStatic]` — each thread gets its own copy of the field.
- An actor's mailbox: the actor's state is mutated only inside its own loop reading from a channel.

```csharp
// Per-thread random number generator.
static readonly ThreadLocal<Random> _rng = new(() => new Random(Environment.TickCount));

int Next() => _rng.Value!.Next();
```

This is *safer than rung 2 in some ways* — it admits mutation without locks — but only because there's a strong discipline that no other thread sees the state.

## Rung 4 — Single-writer state

Many readers, exactly one writer. Solvable with `Volatile.Read`/`Volatile.Write` (release-acquire ordering) for atomic types — and for references to immutable objects, atomic publication via reference assignment.

```csharp
public sealed class HotConfig
{
    private static Configuration _current = new("https://default", 3);

    public static Configuration Current => Volatile.Read(ref _current!);

    public static void Update(Configuration next) => Volatile.Write(ref _current!, next);
}
```

Readers are lock-free; writes are atomic publication of a new immutable object. No reader ever sees a half-built object because reference assignment of a properly-aligned reference is atomic on 64-bit (per ECMA-335 §I.12.6.6).

This pattern shows up everywhere — hot config, current session, latest snapshot, last known value. When you can convince yourself there's only one writer, this is the cheapest safe pattern in the runtime.

## Rung 5 — Synchronised mutable state

Many readers and writers, protected by locks/atomics. The hard case.

```csharp
private readonly object _gate = new();
private readonly Dictionary<int, User> _users = new();

public User? Get(int id)
{
    lock (_gate) return _users.TryGetValue(id, out var u) ? u : null;
}

public void Set(User user)
{
    lock (_gate) _users[user.Id] = user;
}
```

Correct. Costly under contention. The lock cost is fine when contention is rare; under hot contention you're better off with rung 4 (snapshot + atomic publication) or a `ConcurrentDictionary` (which uses fine-grained striping internally).

## Rung 6 — Unsynchronised mutable state

A bug. There is no rung 6 in production code; if you find yourself here, climb back up.

```csharp
// All bugs from this point on.
public class Counter
{
    private int _value;
    public void Increment() => _value++;     // race
    public int Get() => _value;              // possibly stale
}
```

The "possibly stale" half deserves emphasis. The bug isn't only "lost updates from concurrent increments" — it's also "a reader might never see the latest write" because the JIT is allowed to keep `_value` in a register across the body of a method that doesn't have any synchronisation. That's the memory-visibility issue from the next section.

## Picking a rung

A practical decision flow:

```
Can the data be a pure function input?        → Rung 1
Can the data be constructed once and frozen? → Rung 2
Is the data only ever touched by one thread? → Rung 3
Is there one writer and many readers?         → Rung 4 (Volatile.Read/Write + immutable snapshot)
Are there many writers, and the work item is small? → Rung 5 (lock or ConcurrentX)
None of the above?                            → Re-examine the design. You probably want to confine or immutabilise.
```

Most production code that needs concurrency lives on rungs 2, 3, or 4. Rung 5 is unavoidable for some shapes — caches, in-process counters, queues — but every time you reach for it, it's worth asking whether a rung 4 design with `ImmutableDictionary` and atomic replacement would do the job.
