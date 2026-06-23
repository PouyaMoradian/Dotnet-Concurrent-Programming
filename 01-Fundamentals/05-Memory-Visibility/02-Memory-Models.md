# Memory models — what each layer guarantees

A "memory model" is the contract between a programmer and a system (hardware, compiler, runtime) about which reorderings of memory operations are allowed. .NET's full picture is a stack of three contracts: the hardware's model, the JIT's model, and the language's expression of those.

## The hardware models

| Architecture | Model | Allows |
|---|---|---|
| **x86 / x86-64** | **TSO (Total Store Order)** | Store-load reordering only |
| **ARM64** | **Weak** | Almost any reordering between independent operations |
| **POWER** | **Weak** | Even more aggressive than ARM |
| **RISC-V** | Configurable (RVWMO) | Roughly ARM-like in practice |

x86 is famously the strongest mainstream model: stores are seen by other cores in program order, and loads cannot be reordered past prior loads. The one reordering it allows — a store followed by a load to a different address may appear to other cores as the load happening first — is what breaks Dekker's algorithm without an `MFENCE`.

ARM64 is weak: any independent load or store can be reordered with respect to any other. To get the equivalent of x86's defaults you need explicit `DMB ISHST`/`DMB ISHLD` instructions, or use load-acquire / store-release variants (`LDAR`, `STLR`).

The practical consequence: code that "happens to work" on x86 because the hardware is forgiving can fail on ARM64 because the hardware isn't. Apple Silicon, AWS Graviton, and most cloud ARM hosts will exercise these cases in production. **If your code is going to run on a server, test it on ARM.**

## The CLR memory model

ECMA-335 specifies the CLR's memory model. Modern .NET (since .NET 6 / Core 2.x) further tightened the *implementation* — but for portable code, rely on the *specification*, not the implementation. The specified guarantees are:

1. **Atomic reads and writes of primitive types ≤ pointer-sized.** This means `int`, `bool`, `IntPtr`, references, etc., are atomic with respect to value tearing. `long` and `double` are *not* guaranteed atomic on 32-bit runtimes; use `Interlocked.Read` for portability.
2. **`volatile` reads are acquires; `volatile` writes are releases.** Same for `Volatile.Read`/`Volatile.Write`. The pair establishes happens-before between writer and reader.
3. **Locked regions form a full barrier.** Entering a `lock` acquires; exiting releases.
4. **No reorderings of side effects past synchronisation primitives.** A `volatile` write can't be moved later past another volatile write; a volatile read can't be moved earlier past another volatile read.

The CLR's *implementation* on x86 is stricter than the spec requires — for example, normal reads on x86 already behave like acquires. But the JIT is allowed to reorder around them, and on ARM64 the hardware doesn't give you the acquire for free. So programming to the spec, not the implementation, is the only way to write code that runs everywhere.

## How this maps to ECMA-335 §I.12.6

The relevant clauses (paraphrased):

- §I.12.6.6 (*Atomic reads and writes*) — "A conforming CLI shall guarantee that read and write access to properly aligned memory locations no larger than the native word size … shall be atomic." (No tearing.) This same clause is what makes reference assignments atomic.
- §I.12.6.7 (*Volatile reads and writes*) — optimisations that change the relative order of volatile operations are not allowed; "volatile reads have acquire semantics … volatile writes have release semantics."

(Note: §I.12.6.2 is *Alignment* and §I.12.6.4/.5 are *Optimization* / *Locks and threads* — the atomicity and volatile guarantees live in .6 and .7.)

You won't need to recite these clauses; you do need to internalise the underlying facts. Atomic reference assignment (clause §I.12.6.6) is probably the most useful single fact — it's what makes the lock-free "copy-on-write + atomic publication" pattern work.

## Sequential consistency, why it's not the default, and why it doesn't matter much

The textbook strongest model is **Sequential Consistency (SC)**: "the program executes as some interleaving of the operations on each thread, and each thread's operations appear in program order." It's intuitive and almost no real system gives it to you cheaply — even x86 doesn't, hence the store-load reordering.

The practical model you should program to is **Release-Acquire (RA)**: pair a release-store on the producer side with an acquire-load on the consumer side, and you establish happens-before between everything *before* the store and everything *after* the load. RA is sufficient for nearly every concurrency pattern in this repo. It's also what `Volatile.Write` / `Volatile.Read` deliver.

If you really do need SC for a specific pattern (Dekker, certain peer-to-peer synchronisations), the way to get it in .NET is to use `Interlocked.*` operations on both sides — `Interlocked.*` is a full barrier and effectively SC.

## How to test

You can't unit-test a memory model bug in the usual sense; a bug that fires once per million runs doesn't fail in CI. The defences are:

1. **Static analysis.** The Microsoft.VisualStudio.Threading.Analyzers and Roslyn analysers catch many smells (`VSTHRD002`, etc.).
2. **Stress tests on weak hardware.** Run torture loops on ARM64 hardware (Apple Silicon, ARM cloud instances). Bugs that are rare on x86 are common on ARM.
3. **Lincheck-style model checkers.** For data-structure code, tools like *CHESS* (older, Microsoft Research) and *Lincheck* (JVM, but the technique transfers) systematically explore interleavings.
4. **Code review with the rules in your head.** Most memory-model bugs are simple inversions: someone wrote `Volatile.Write(ref x, …)` but `var v = x;` in the reader. The pair is the contract; if you have one without the other, it's broken.

The most reliable defence is the discipline of climbing the ladder of safety: stay on rungs 1-4 where memory model concerns mostly don't apply, and reach for rung 5 (locks) when you can't.
