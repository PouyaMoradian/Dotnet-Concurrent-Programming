# `lock` — the workhorse

`lock(obj) { … }` is the most-used synchronisation primitive in C#. It looks simple. The internals are not.

## What it lowers to

```csharp
lock (sync) { Critical(); }
```

Pre-C# 13, the C# compiler emitted:

```csharp
object _temp = sync;
bool _taken = false;
try { Monitor.Enter(_temp, ref _taken); Critical(); }
finally { if (_taken) Monitor.Exit(_temp); }
```

In C# 13 / .NET 9, when the locked expression is typed as `System.Threading.Lock`, the compiler emits a different pattern: it calls `Lock.EnterScope()` and disposes a `Scope`. This sidesteps `Monitor`'s overhead on the object header. (Old `lock(object)` still compiles to the `Monitor` pattern.)

## The new `System.Threading.Lock` (.NET 9)

A real reference-type lock. Use it instead of `lock(object)` for new code on .NET 9+:

```csharp
private readonly Lock _sync = new();           // System.Threading.Lock

void DoWork()
{
    lock (_sync) { Critical(); }                // compiler picks the Lock-based pattern
}
```

Why prefer it:

- **No accidental `lock(this)` / `lock(typeof(T))`** — the type is purpose-built.
- **Type system catches misuse.** You can't accidentally lock on a string or boxed value.
- **Slightly faster** on contention because the runtime can skip Monitor's object-header accommodations.
- **`using (sync.EnterScope())`** as an alternative form when you want explicit scope.

## What `Monitor` itself does

Each .NET object header has a *sync block index* slot. On the fast (uncontended) path, `Monitor.Enter` does a CAS on that slot. On contention, the runtime allocates (or reuses) a sync block, which holds:

- The owning thread ID (for re-entrance).
- A recursion count (re-entrant locks).
- A waiter list (kernel-event-backed).

Uncontended cost: ~10–20 ns (one CAS, no kernel transition). Contended cost: variable, can include a futex/event wait of ~µs.

## Anti-patterns

```csharp
lock (this)                  // ❌ external code may also lock 'this'
lock (typeof(MyType))        // ❌ cross-AppDomain leakage
lock ("constant string")     // ❌ interned, shared globally
lock (someEnum)              // ❌ boxed each time → different objects
lock (someValueType)         // ❌ boxed each time
```

Always use a private `readonly object` (or `readonly Lock`) field.

## Recursive locking

`Monitor` is reentrant — the same thread can re-enter the same lock. **This is usually a bug indicator**, not a feature. If you find yourself recursing into a locked region, your design has the lock in the wrong place. The flat alternative — extract the inner work to a helper that asserts the lock is held — is almost always cleaner.

## Best practices

1. **Lock around state, not behaviour.** A type, not a class, owns the lock that protects its fields.
2. **Don't call out under the lock.** Calling unknown user code (events, virtual methods, async) while holding a lock invites deadlock.
3. **Don't `await` under the lock.** The compiler forbids it for `lock(object)`; for `Lock` it's a runtime check. If you need async-aware exclusion, use `SemaphoreSlim(1,1)` — see [AsyncLock](../AsyncLock/).
4. **Keep critical sections small.** Compute outside; assign inside.

## Performance

| Scenario | Approx cost |
|---|---|
| Uncontended `lock(object)` round trip | 10–20 ns |
| Uncontended `lock(System.Threading.Lock)` | 8–15 ns |
| Contended (one waiter, short hold) | 100–500 ns |
| Contended (many waiters, kernel wait) | µs |

For the contended cases — which is where it actually matters — the overhead is dominated by *cache-coherence traffic* on the lock object itself, not by the lock's own cost.
