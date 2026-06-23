# Read For Ownership (RFO) and the cost of contention

If you only learn one mechanism from this entire chapter, learn this one: a write to a cache line that other cores have cached costs an interconnect round-trip. Every `Interlocked.Increment` on a shared counter, every `lock` on a contended object, every `volatile` write that another core was reading — they all pay this cost. And when many cores are doing it at once, the cost multiplies non-linearly.

## The transition that costs

The cheap state transitions in **MESI** (the four-state Modified / Exclusive / Shared / Invalid cache-coherence protocol) are local:

- M → M (we keep writing to a line we own).
- E → M (we write to a line nobody else has cached).
- I → E or I → S (we fetch a line we didn't have).

The expensive one is **S → M**, the *upgrade*. Two cores might both have the line in S, reading happily. The instant one wants to *write*, it must:

1. Send an **RFO** (Read For Ownership; on x86 it's a "BusRdX" / "Invalidate" packet) on the interconnect.
2. Wait for all sharers to acknowledge they've dropped their copy (gone to I).
3. Take ownership; transition to M.
4. Perform the write.

On a single-die mesh (Intel Sapphire Rapids) the round trip is **~30–50 ns**. On a multi-die EPYC crossing **CCD**s (Core Complex Dies — chiplets on AMD's multi-die parts) it can be **~100–200 ns**. Across sockets on a **UPI** / **IF** (Intel Ultra Path Interconnect / AMD Infinity Fabric) hop, **200+ ns**. These are wall-clock numbers, not cycles.

## What this looks like as throughput

Compare two programs:

```csharp
// (A) Single shared counter.
private static long _c;
Parallel.For(0, 100_000_000, _ => Interlocked.Increment(ref _c));

// (B) Per-thread counter, summed at the end.
var locals = new ThreadLocal<long>(() => 0, trackAllValues: true);
Parallel.For(0, 100_000_000, _ => locals.Value++);
long total = locals.Values.Sum();
```

(A) is bottlenecked by the RFO loop. Every increment requires the line to be M on the writer's L1, which means it just left another core's L1. Throughput per core *decreases* as you add more cores, because the line spends more time in flight.

(B) is bottlenecked by the L1 write throughput on each core's *own* cache line. There is no cross-core traffic. Throughput per core stays constant; total throughput scales linearly.

Typical measurements on an 8-core x86 box, 100M increments:

| Cores | (A) shared counter | (B) per-thread |
|---|---|---|
| 1 | 0.45 s | 0.10 s |
| 2 | 1.2 s | 0.05 s |
| 4 | 2.6 s | 0.025 s |
| 8 | 5.0 s | 0.012 s |

Going from 1 to 8 cores **makes (A) ~11× slower**. (B) scales by 8× as you'd hope. Same logical operation, same number of increments — only the location of writes differs.

## The "scaling cliff"

Below ~2 contending cores, locked operations are cheap enough to ignore. From ~4 cores up, the cost grows roughly linearly with sharers, *and* the OS scheduler starts to make things worse: when a thread blocks on a **CAS** (Compare-And-Swap — atomic read-compare-write, the building block of `Interlocked.CompareExchange`) retry, it gets descheduled, its cache state is invalidated, and on resume it has to re-fetch.

This is why scaling on shared mutable state is fundamentally bounded — you can't optimise your way around a protocol that has to ask N cores for permission.

## Reads are not free either — but they're close

A read of a line in I state still requires a fetch — typically ~12 cycles if the line is in L2, ~40 cycles if L3, ~80 ns if DRAM. But if you're reading something multiple cores read, the lines stay in S in each one's L1 and the reads cost ~4 cycles thereafter.

The asymmetry of reads vs writes is the *entire* basis of:

- **Read-Copy-Update (RCU)** in the Linux kernel — readers never block; writers copy.
- **Immutable persistent data structures** in .NET (`ImmutableList<T>`, F# records). Readers share; writers allocate.
- **`ConcurrentDictionary<TKey,TValue>`'s use of sharded locks** — reads are mostly lock-free; writes lock a stripe.

## .NET's contended primitives, ranked by cost

| Primitive | Cost (uncontended) | Cost (heavily contended) |
|---|---|---|
| Regular field read | <1 ns | <1 ns |
| `Volatile.Read` | <1 ns | <1 ns |
| `Volatile.Write` | <1 ns | ~5–50 ns (RFO) |
| `Interlocked.Increment` | ~5–10 ns | ~50–200 ns |
| `Interlocked.CompareExchange` | ~5–10 ns | ~50–500 ns (CAS retry loop on failure) |
| `lock` (`Monitor.Enter`) | ~20 ns | thousands of ns (sleep + wake + scheduler) |
| `SemaphoreSlim.WaitAsync` | ~50 ns | similar to `lock`, plus async machinery |

Note that *uncontended* atomic ops are cheap — about 5–10× a regular read. *Contended* atomic ops are dominated by RFO traffic and can be 10–100× worse. The cost is data-dependent; the same code runs at very different speeds depending on whether anyone else is on the line.

## Two general fixes

### 1. Shard the write set

Replace one line that all writers fight over with N lines, one per writer:

```csharp
// Pseudo-code; see 04-DotNet-Patterns.md for the real version.
class ShardedCounter
{
    private readonly PaddedLong[] _shards;
    public ShardedCounter() => _shards = new PaddedLong[Environment.ProcessorCount];

    public void Increment()
        => Interlocked.Increment(ref _shards[Thread.GetCurrentProcessorId() % _shards.Length].Value);

    public long Sum()
    {
        long s = 0;
        for (int i = 0; i < _shards.Length; i++)
            s += Interlocked.Read(ref _shards[i].Value);
        return s;
    }
}
```

Read costs become O(N) but happen rarely; writes become O(1) and parallel.

### 2. Hold writes in thread-local state, publish in batches

If you only need the aggregate occasionally:

```csharp
[ThreadStatic] private static long _local;

void Hit() { _local++; }

void Drain(ref long shared) { Interlocked.Add(ref shared, _local); _local = 0; }
```

Hit is now a register/L1 update. Drain runs on a timer or at the end of a phase. The expensive part — the cross-core write — happens at a rate you control, not at the rate of incoming events.

## When contention is unavoidable

Some algorithms genuinely need single-point synchronisation: a unique-ID generator, a global epoch counter, a bounded queue's head/tail. For these:

- Use `Interlocked.Increment`/`CompareExchange` — they're the cheapest correct option.
- If the contention is *bursty*, consider an exponential backoff in the CAS loop (`SpinWait`).
- If you need *fairness* under heavy contention, fall back to a queue-based lock (`SpinLock`, or third-party FIFO locks).

A bare `Interlocked.Increment` on a heavily contended line is not "wrong" — it's just slow. Pick it knowingly.

## Practical takeaways

- **Contention is the only place writes get really expensive.** Uncontended atomic ops are fine; contended ones cost interconnect round-trips.
- **Add cores, lose throughput.** Counterintuitive, but true for shared-line write paths.
- **The fix is layout, not algorithm.** Sharding or thread-local accumulation typically restores linear scaling.

## Lab

```bash
dotnet run --project 00-Prerequisites -- 7
```

`ContendedInterlockedDemo` runs the (A)/(B) experiment above with configurable thread counts and reports per-core throughput.

## Further reading

- **Cliff Click — *A lock-free hash table*** (talk + paper) — for how to design lock-free structures that *don't* live on the contention cliff.
- **Dice, Marathe, Shavit — *Lock cohorting*** — algorithms that group contenders by NUMA node so the line stays local to a node.
- **Heinz Doofenshmirtz Memorial Concurrency Reading List**: ahem, no, see the references in `04-DotNet-Patterns.md`.
