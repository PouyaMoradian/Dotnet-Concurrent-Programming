# SpinLock

A `SpinLock` is a struct-based lock that *spins* in a loop instead of parking the thread. It exists for a specific niche:

- **Critical section is a handful of instructions** (≤100 ns of work).
- **Contention is real but brief** — busy-wait will resolve faster than a kernel wait.
- **You want to avoid allocation.** `SpinLock` is a struct; no GC.

## Don't use it as a default

For any critical section that:

- Includes a method call you don't fully control;
- Holds longer than ~1 µs in the worst case;
- Operates under unpredictable contention;

`SpinLock` will burn CPU spinning for the full duration of the worst-case hold. **Stick with `lock` / `Lock` unless you have a benchmark that proves otherwise.**

## Usage

```csharp
SpinLock spin = new(enableThreadOwnerTracking: false); // tracking is for debug; off for perf

var taken = false;
try
{
    spin.Enter(ref taken);
    // critical section — keep it tiny
}
finally
{
    if (taken) spin.Exit(useMemoryBarrier: false);
}
```

`enableThreadOwnerTracking: false` removes a per-acquire write of the owning thread. Saves cycles; loses the ability to detect re-entrance (which `SpinLock` does **not** support; if you re-enter you deadlock).

`useMemoryBarrier: false` on `Exit` skips the explicit barrier; relies on the implicit ordering provided by the unlock CAS. Safe if the data being protected is itself accessed via volatile/locked operations.

## Where it actually wins

- **Inside other primitives.** The BCL uses `SpinLock`-like patterns inside `ConcurrentQueue`'s segments and parts of the thread pool.
- **Hot paths in lock-free data structures** where you fall back to a tiny lock for a slow path.
- **Real-time-ish patterns** where you've reserved cores and don't need the scheduler's help.

## How spin escalation works

`SpinLock` *doesn't* escalate to a kernel wait — it spins forever (well, until the timeout you pass). For a self-escalating lock, use `Monitor` (which spins for a few iterations of `SpinWait` before parking) or `lock` directly.

## Cost (uncontended)

Around 5–15 ns per Enter/Exit pair, faster than `lock` because there's no Monitor sync block, no event handle. Under contention, the cost is whatever you spent burning cycles waiting plus the CAS round trip.

## Anti-pattern

```csharp
// ❌ field-stored SpinLock copied on access
class Bad
{
    public SpinLock S;          // public mutable struct field — copies break it
}
```

`SpinLock` is a mutable struct. Pass it by `ref`; never copy. Make it `private readonly` *only if you initialise once and reference via `ref this._lock`* — note the readonly-ref subtlety. The simplest pattern: `private SpinLock _lock = new(false);` and access through `ref _lock`.
