# CPU reordering

The CPU is allowed to execute and retire instructions in an order that doesn't match what's in memory — as long as the *single-thread observable* behaviour is the same. To other threads, this means writes can become visible in a different order than the source code wrote them.

## x86 — TSO (Total Store Order)

x86 has the *strongest* commercially relevant memory model.

| Reordering | Allowed? |
|---|---|
| `LoadLoad` (load before load) | no |
| `StoreStore` (store before store) | no |
| `LoadStore` (load before store) | no |
| `StoreLoad` (store before load) | **yes** |

That last one is the famous one. The "store buffer" lets a core retire a store before it's been broadcast to other caches; a subsequent load can read from another location *before* the buffered store becomes globally visible. This is the **only** reordering on x86, and it's why Dekker's-algorithm-style code needs an explicit `mfence`.

## ARM64 — weakly ordered

Almost any reordering allowed unless explicitly prevented. Provides:

- **`LDAR`** — load with acquire; later memory ops can't reorder before.
- **`STLR`** — store with release; earlier ops can't reorder after.
- **`DMB ISH`** — full inner-shareable barrier; nothing can cross.

The .NET JIT emits these for `Volatile.Read`/`Volatile.Write`/`Interlocked.*`, so your code that uses those is correct on ARM64. Code that *doesn't* and relied on x86 quirks is not.

## How this manifests in C#

```csharp
class C
{
    int x, y;

    void Producer() { x = 1; y = 2; }
    int Consumer()  { return y == 2 ? x : -1; }     // can return 0 on ARM64
}
```

On ARM64, `y = 2` may become visible before `x = 1`. The consumer reads `y == 2` and then `x` which is *still 0* in another core's view. The fix:

```csharp
void Producer() { x = 1; Volatile.Write(ref y, 2); }
int Consumer()  { return Volatile.Read(ref y) == 2 ? x : -1; }
```

The release-acquire pair forbids the reordering.

## Practical demo (rare but real)

Dekker's algorithm is the canonical example where StoreLoad reordering matters:

```csharp
static int flag1, flag2;

void Thread1()
{
    flag1 = 1;
    if (flag2 == 0) Critical();   // can reorder; both threads can enter Critical
}

void Thread2()
{
    flag2 = 1;
    if (flag1 == 0) Critical();
}
```

The fix: insert a full barrier between the store and the load:

```csharp
void Thread1()
{
    flag1 = 1;
    Interlocked.MemoryBarrier();    // forbids StoreLoad
    if (flag2 == 0) Critical();
}
```

In modern code, you wouldn't roll Dekker's algorithm — `lock` does it. But this is the shape of the bug `Interlocked.MemoryBarrier` is for.

## Takeaways

- Don't reason from "the code looks sequential". The CPU disagrees.
- Don't rely on x86 specifics. Code "works" on x86 but breaks on ARM64.
- Use `Volatile`/`Interlocked`/`lock` and the model is sane.
