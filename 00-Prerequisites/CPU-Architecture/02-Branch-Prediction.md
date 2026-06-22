# Branch prediction

Modern CPUs are deep pipelines. By the time the *first* µop of a branch instruction reaches Execute, the front end has already fetched and decoded the next ~20 instructions. If the branch turns out to go the other way, all of that work has to be thrown away — the pipeline is **flushed**, the redirected target is fetched cold, and the core sits idle for ~15–20 cycles. Multiply that by a tight loop's iteration count and you have a 3–6× difference from a *correctness-neutral* change to a predicate.

The CPU therefore *predicts* every branch and keeps fetching past it speculatively. The predictor is one of the most engineered parts of the chip — modern designs use multi-level perceptron-style predictors, BTBs (Branch Target Buffers), and indirect target predictors. The accuracy on real workloads is typically >95%.

## The canonical demo: sorted vs unsorted

This is the StackOverflow-famous example (it earned ~30k upvotes a decade ago and the answer is still right):

```csharp
var rng = new Random(0);
int[] a = new int[1_000_000];
for (int i = 0; i < a.Length; i++) a[i] = rng.Next() % 256;

// Optional: Array.Sort(a);

long sum = 0;
for (int i = 0; i < a.Length; i++)
{
    if (a[i] >= 128) sum += a[i];
}
```

Same number of comparisons, same number of additions, same memory accesses. Sorted vs unsorted, the sorted version is **3–6× faster** on x86 and Apple Silicon. Why?

In the unsorted case, `a[i] >= 128` is true ~50% of the time, in a random pattern. The predictor can't lock on. Every couple of iterations it mispredicts and flushes the pipeline.

In the sorted case, the predicate is `false, false, …, false, true, true, …, true`. After a few iterations the predictor settles on each side. Mispredict rate drops to ~0. The pipeline stays full.

Demo 4 (`BranchPredictionDemo`) measures this on your machine.

## What the predictor actually looks for

Modern predictors are *history-based*: they don't just remember "this branch took T last time", they remember the *pattern* of the last N taken/not-taken outcomes for this branch *and* surrounding branches (Global History Register). This is why deeply unbalanced trees, where one path is hot, predict almost perfectly — the predictor learns "after I came down this path, this next branch is always taken".

It also tracks **indirect branches** (virtual calls, function pointers, switch tables) through a Branch Target Buffer (BTB). A polymorphic call site with one dominant target is predicted perfectly; a megamorphic one (4+ targets equally likely) is a steady stream of mispredicts. This is one reason **PGO**-driven *devirtualisation* (Profile-Guided Optimisation — the JIT collects which targets a virtual call actually hits at tier-0 and rewrites the call at tier-1) matters in .NET 8+: turn an indirect call into a direct call, and the predictor goes from "guess from history" to "trivially correct".

## How to write predictor-friendly code

### 1. Sort, when sorting is free

If you have to scan a column anyway and your downstream stages benefit from order, sorting first can pay for itself many times over. This is the principle behind columnar database engines.

### 2. Use branchless tricks for tight, predictable-shaped inner loops

Two equivalent ways to compute `Math.Max(a, b)`:

```csharp
// Branchy
int max1 = a > b ? a : b;

// Branchless (compiles to cmov on x86)
int max2 = a - ((a - b) & ((a - b) >> 31));   // beware: a - b can overflow for far-apart operands
```

On modern x86 the JIT often emits `cmov` for the ternary anyway, which is itself branchless. Check the disassembly before deciding the rewrite is worth the legibility loss.

The classic readable branchless idiom in C#:

```csharp
// Count elements where a[i] >= 128, branchlessly.
int count = 0;
for (int i = 0; i < a.Length; i++)
    count += (a[i] >= 128) ? 1 : 0;     // JIT often emits SETcc + ADD; no branch
```

vs

```csharp
int count = 0;
for (int i = 0; i < a.Length; i++)
    if (a[i] >= 128) count++;            // Branch; performance depends on data
```

On data that's random, the branchless version wins big. On data that's almost always one-sided, the branch version wins (the branch predicts perfectly, and you skip the increment when not needed).

### 3. Hoist rare/cold cases out of the inner loop

```csharp
foreach (var item in items)
{
    if (UnlikelyError(item)) Throw(item);   // ⟵ never inlined
    Process(item);
}
```

If `UnlikelyError` always returns false, the branch predicts perfectly and the loop runs straight. Mark cold paths `[MethodImpl(MethodImplOptions.NoInlining)]` and the JIT will lay them out off the hot path.

### 4. Avoid megamorphic dispatch in hot loops

```csharp
foreach (var visitor in visitors)
    visitor.Accept(node);   // ⟵ if visitors have many concrete types, BTB misses
```

If you have a small fixed set of visitor types, prefer a `switch` on a type discriminator, or generate a dispatcher per concrete type. .NET 8's dynamic PGO will devirtualise at one or two hot targets but not more.

### 5. Let dynamic PGO help you

On .NET 8+, tiered compilation collects type-feedback at tier-0 and emits devirtualised, inline-cached call sites at tier-1. The runtime does this transparently. You only need to *not* fight it — i.e., don't force tier-1 compilation by disabling tiering unless you know why.

## What it costs you to mispredict

| Architecture | Mispredict cost (cycles, approx) |
|---|---|
| Intel Skylake/IceLake | 15–20 |
| AMD Zen 4 | ~13 |
| Apple M-series | 12–16 (deep pipeline, but wide recovery) |
| Cortex-A78 | 11–13 |

At 3 GHz that's roughly 4–8 ns per mispredict. A loop that mispredicts every iteration runs at ~3 ns/iteration on top of any real work. Sustained mispredicts double or triple the time of a memory-bound loop.

## How to spot mispredicts in production

Linux:
```bash
perf stat -e cycles,instructions,branches,branch-misses ./yourapp
# Look for branch-misses / branches > 1%.
```

Windows: PerfView's CPU counters can be configured to include `MISPREDICTED_BRANCH_RETIRED`.

BenchmarkDotNet:
```csharp
[HardwareCounters(HardwareCounter.BranchMispredictions, HardwareCounter.BranchInstructions)]
public class MyBench { ... }
```

## Practical takeaways for .NET

- Most code is branch-predict-friendly *by accident*; you don't need to think about this until a profiler tells you to.
- When you do: the data is the cause, not the source code. Predicates on random-shaped data are the culprit.
- The fastest branch is the one the JIT removed; the second fastest is the one that always predicts the same way. The third is `cmov`. The slowest is a random conditional jump.
- Devirtualisation is a branch-prediction story too — turning a BTB-dependent indirect call into a direct one.

## Lab

```bash
dotnet run --project 00-Prerequisites -- 4
```

`BranchPredictionDemo` runs the sorted-vs-unsorted experiment with timing for both. Expected: sorted runs ~3–6× faster on x86-64, ~2–4× on Apple Silicon (its predictor is excellent so the gap narrows).

## Further reading

- **Lin et al. — *Perceptron-based branch prediction*** (the algorithm behind modern predictors).
- **Travis Downs** — "Branch prediction" series on `travisdowns.github.io`.
- **Daniel Lemire** — *Sometimes branching has a cost* (great practical post with numbers).
