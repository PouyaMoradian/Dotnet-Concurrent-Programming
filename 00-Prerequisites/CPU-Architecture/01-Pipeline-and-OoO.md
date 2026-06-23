# The pipeline and out-of-order (OoO) execution

A modern x86-64 or ARM64 core looks, from the outside, like the textbook von Neumann machine: it executes instructions one after another in program order. Inside, it is anything but. A Skylake-class Intel core, an Apple M-series core, a Zen 4 core — they're all wide, deep, speculative, **out-of-order** (OoO), register-renamed engines pretending to be a 1970s CPU.

The pretence is the key to performance and the source of every "but the spec says…" surprise in concurrency.

## The pipeline at a glance

Roughly (numbers from Intel Skylake; AMD Zen 4 and Apple Avalanche are similar in shape):

```
                     ┌──────────────┐
   branch predictor →│   Fetch      │  16 bytes/cycle from L1i
                     └──────┬───────┘
                            │
                     ┌──────▼───────┐
                     │   Decode     │  → up to 5 µops per cycle
                     └──────┬───────┘
                            │
                     ┌──────▼───────┐
                     │   Rename     │  ≥ 200 physical registers, hides false deps
                     └──────┬───────┘
                            │
                     ┌──────▼───────┐
                     │   Schedule   │  ROB ~224 µops in flight
                     └──────┬───────┘
                            │
            ┌───────────────┼─────────────────┐
            ▼               ▼                 ▼
       ┌────────┐      ┌────────┐        ┌─────────┐
       │ ALU(s) │      │ AGU(s) │        │ FPU/SIMD│        Execute (multiple ports)
       └────┬───┘      └────┬───┘        └────┬────┘
            │               │                 │
            └───────────────┼─────────────────┘
                            ▼
                     ┌──────────────┐
                     │   Retire     │  in program order
                     └──────────────┘
                            ▼
                     ┌──────────────┐
                     │ Store buffer │  retired stores wait here to drain to L1
                     └──────────────┘
```

(ALU = Arithmetic Logic Unit; AGU = Address Generation Unit; FPU = Floating-Point Unit; SIMD = Single-Instruction, Multiple-Data vector unit; ROB = Re-Order Buffer.)

Three knobs explain almost every micro-optimisation discussion you've ever had:

1. **Width.** The core can decode/issue/retire multiple instructions per cycle. Skylake retires up to 4; Zen 4 retires 6+; Apple M-series retires 8. This is what "**superscalar**" means.
2. **Depth.** The window of in-flight instructions ("re-order buffer", ROB) is hundreds wide. The CPU is looking far ahead of where program order says it is.
3. **Speculation.** The CPU follows predicted branches and rolls back if they were wrong. Without this the deep pipeline would stall on every conditional.

## Instruction-Level Parallelism (ILP)

The CPU finds independent µops in the window and dispatches them to *separate ports* in the same cycle. Two adds, a load, and a compare can all be in flight at once.

But this only works if the µops *are* independent. The killer pattern:

```csharp
long sum = 0;
for (int i = 0; i < N; i++) sum += a[i];
```

Each iteration depends on `sum` produced by the previous iteration. The pipeline can't issue the next `add` until the previous one finishes. It serialises.

The fix is *manual unrolling with multiple accumulators*:

```csharp
long s0 = 0, s1 = 0, s2 = 0, s3 = 0;
int i = 0;
for (; i + 3 < N; i += 4)
{
    s0 += a[i + 0];
    s1 += a[i + 1];
    s2 += a[i + 2];
    s3 += a[i + 3];
}
long sum = s0 + s1 + s2 + s3;
for (; i < N; i++) sum += a[i];
```

Now four chains of dependent adds run in parallel through the pipeline. On a 4-wide retire engine, this is a ~3.5–4× speed-up *with the same algorithm and the same memory accesses*. Demo 5 (`InstructionLevelParallelismDemo`) shows it.

The same trick is what an **FMA**-aware (Fused Multiply-Add — a single instruction that computes `a * b + c`) autovectoriser already does for you when the loop is simple. But the moment you reach for something the JIT can't autovectorise (variable-length, branching, polymorphic), you need to know about ILP manually.

## Register renaming — the magic that makes out-of-order work

The C# `for (int i = 0; i < N; i++)` writes to `i` every iteration. If the CPU literally reused the architectural register for `i`, it could never overlap iterations: the second iteration's `i++` would have to wait for the first's. Register renaming solves this by maintaining a much larger pool of *physical* registers (~256 on Skylake), assigning a fresh one for each write. The architectural name `i` is a moving pointer into the pool. This is how "false dependencies" — writes to the same name that have no actual data dependency — get optimised away.

You don't write code to *use* renaming; you write code to *not defeat* it. The way to defeat it is to introduce a real read-modify-write loop on the same memory location (e.g. an `Interlocked.Increment` on a shared counter — the cache line itself becomes the bottleneck, not the renamer).

## Memory loads and the load/store unit (LSU)

A load goes through the **Load Buffer**. A store goes through the **Store Buffer**. Both let the core retire the instruction without waiting for the cache:

- Load: the µop retires "speculatively" — the data isn't there yet, but anything downstream that depends on it is held by the scheduler. When the line arrives, dependents fire.
- Store: the µop retires once the address and data are known; the actual write to L1 happens *later* when the store buffer drains.

This is why **a store you "did" five instructions ago might not be visible to another core yet**. The store buffer is the entire reason x86's TSO (Total Store Order) model allows store→load reorderings. We unpack this in [04-Store-Buffer-and-Memory-Ordering.md](04-Store-Buffer-and-Memory-Ordering.md).

## What a stall looks like in numbers

Roughly, on a 4 GHz x86 core:

| Event | Cost (cycles) | Cost (wall) |
|---|---|---|
| Useful retirement | 4 µops / cycle | ~1 ns per 4 ops |
| L1 hit load | 4 | ~1 ns |
| L2 hit load | 12 | ~3 ns |
| L3 hit load | 40 | ~10 ns |
| DRAM miss | 250–600 | ~80–200 ns |
| Branch mispredict | 15–20 | ~4–5 ns |
| Pipeline flush (e.g. interrupt) | hundreds | hundreds of ns |

The two leftmost columns are why micro-optimisations matter on hot paths *and* why coarser ones (avoid the DRAM miss in the first place) matter much more.

## Practical takeaways for .NET

- **Don't write long dependent chains in hot loops.** Multiple accumulators are free wins.
- **Don't fear `Span<T>`-style indexing.** The JIT removes bounds checks on patterns it can prove safe (`for (int i = 0; i < span.Length; i++)`). It cannot remove them on `i < some.Length + 1` or other patterns it can't prove.
- **Inline what's hot.** The JIT inlines small methods aggressively but capped by IL size; very hot tiny helpers benefit from `[MethodImpl(MethodImplOptions.AggressiveInlining)]`. Don't slap it everywhere — bloat hurts the icache.
- **Pattern that defeats the OoO engine: a fast-path/slow-path branch where the slow path is rare.** Use `if (cond) Slow();` with `Slow` not inlined. The mainline stays straight and dense; the slow path lives elsewhere.

## Lab

Run demo 5 (`InstructionLevelParallelismDemo`):

```bash
dotnet run --project 00-Prerequisites -- 5
```

You should see ~3–4× speedup from the four-accumulator version on x86-64 and Apple Silicon. If you see less, your CPU might already be SIMD-ing the simple version (some Zen variants do); add `--no-vector` flag in a fork, or check the disassembly with `DOTNET_JitDisasm`.

## Further reading

- **Agner Fog's microarchitecture manual** — the only reference that goes deep on pipeline widths, port assignments, and stall sources.
- **Travis Downs's blog (`travisdowns.github.io`)** — astonishingly clear analyses of real CPU effects, with measurements.
- **Daniel Lemire** — *Number parsing at a gigabyte per second* and friends, all of which are case studies in pipeline-friendly code.
