# ReaderWriterLockSlim

The promise: many readers in parallel, exclusive writers. The reality: it's only faster than `lock` when the read critical section is *long* and the read/write ratio is *very* high — and using it correctly is harder than people remember.

## When to use it

You'll know because you've measured. Approximate rule of thumb:

- Reader critical section is **>500 ns of work**.
- Reads outnumber writes **at least 10:1**, ideally 100:1.
- Reader concurrency is real (cores idle while reading would help).

If any of those is borderline, just use `lock` (or `Lock` on .NET 9). The bookkeeping in `RWLockSlim` is significantly heavier than a basic monitor; for short critical sections it loses.

## API

```csharp
var rw = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);

rw.EnterReadLock();
try { /* read */ } finally { rw.ExitReadLock(); }

rw.EnterUpgradeableReadLock();
try
{
    if (NeedsWrite())
    {
        rw.EnterWriteLock();
        try { /* write */ } finally { rw.ExitWriteLock(); }
    }
}
finally { rw.ExitUpgradeableReadLock(); }

rw.EnterWriteLock();
try { /* write */ } finally { rw.ExitWriteLock(); }
```

## The upgradeable mode is the trap

`EnterUpgradeableReadLock` is a *single-writer* lock that lets you *promote* to a write lock without first releasing. **At most one upgradeable reader can be present at a time**, even though plain readers can also be active. So if two threads both `EnterUpgradeableReadLock`, the second blocks. People forget this and design code that serialises through the upgradeable slot.

## Recursion policy

`LockRecursionPolicy.NoRecursion` — recommended. The same thread cannot re-enter. Catches reentrance bugs at runtime instead of letting them hide.

`LockRecursionPolicy.SupportsRecursion` — needed only when you have call paths that legitimately re-acquire. Adds bookkeeping cost and potential for upgrade deadlocks.

## Writer starvation

By default, `RWLockSlim` is *writer-preferring* — a pending writer blocks new readers from entering. This avoids writer starvation but means a long-running reader can hold up writers, which then hold up subsequent readers. Long reader critical sections are pathological.

## Modern alternatives

For reader-heavy maps:

- **`ImmutableDictionary` + atomic-swap** (CoW). Readers lock-free; writers replace the whole map. No reader-writer lock needed.
- **`ConcurrentDictionary`** for general-purpose. Built-in striped locking that handles read-heavy and mixed.
- **`FrozenDictionary` (.NET 8)** if the data is *truly* read-only after build.

For caches:

- **`Microsoft.Extensions.Caching.Memory.MemoryCache`** uses concurrent collections internally; no RWLockSlim needed in your code.

## Performance comparison (in this chapter's `RwLockDemo`)

```
  --- readers=8 writers=0 ---
   lock        : reads=     8,432,011  writes=        0
   RWLockSlim  : reads=    63,001,238  writes=        0     ← real benefit at high reader count

  --- readers=8 writers=1 ---
   lock        : reads=     7,901,002  writes=  812,000
   RWLockSlim  : reads=    21,003,001  writes=  734,000     ← still a win at 8:1 read/write

  --- readers=1 writers=8 ---
   lock        : reads=     1,201,001  writes= 9,801,002
   RWLockSlim  : reads=     1,002,003  writes= 7,300,001    ← write-heavy: lose to lock
```

(Numbers vary; rerun on your hardware.)

The takeaway: **measure**. RWLockSlim is the right answer for a narrow band of workloads and the wrong answer everywhere else.
