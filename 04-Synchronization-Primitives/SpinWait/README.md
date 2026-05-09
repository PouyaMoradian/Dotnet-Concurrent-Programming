# SpinWait — the "polite spin"

`SpinWait` is a struct that helps you spin without being a CPU hog. It encapsulates the strategy:

1. **Tight `Thread.SpinWait(...)` loops** for the first few iterations — the CPU's `pause` instruction (x86) or `yield` (ARM) hints to the core that this is a spin loop, easing pipeline pressure on the SMT sibling.
2. **`Thread.Yield()`** after a few spins — give equal-priority threads on this core a chance.
3. **`Thread.Sleep(0)`** — yield to any runnable thread, even lower priority.
4. **`Thread.Sleep(1)`** — give up the rest of the quantum.

This escalation is what makes lock-free retry loops *not* turn into priority-inversion or hyper-thread-monopolisation traps.

## Usage in a lock-free retry

```csharp
var sw = new SpinWait();
while (true)
{
    var snapshot = _state;
    var next = Compute(snapshot);
    if (Interlocked.CompareExchange(ref _state, next, snapshot) == snapshot) break;
    sw.SpinOnce();        // back off politely on contention
}
```

That `SpinOnce()` is the difference between a tight CAS loop that pegs your core and a retry loop that gracefully scales.

## `SpinUntil`

A static helper for "spin politely until predicate":

```csharp
SpinWait.SpinUntil(() => Volatile.Read(ref _ready), TimeSpan.FromSeconds(1));
```

It's a building block for things like waiting for a flag without a kernel event.

## When to use SpinWait directly

- **Inside lock-free retries.** As shown above.
- **Inside custom primitives** where you want bounded spinning before an expensive escalation.
- **Probably nowhere else.** For "wait for X to happen" you almost always want an event/semaphore/Task. SpinWait spends CPU; events don't.

## Cost note: SMT-friendly

A naive `while (!ready) { }` spin pegs the entire core including the SMT sibling. Replacing it with `SpinWait` reduces the impact on the sibling — `pause` / `yield` lets the sibling thread make progress. This is why every BCL primitive's user-mode fast-path uses `SpinWait`-like patterns under the hood.
