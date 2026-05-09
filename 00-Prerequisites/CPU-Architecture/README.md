# CPU architecture for .NET developers

A modern x86-64 / ARM64 CPU is not a deterministic instruction-fetch machine; it is a *speculative, pipelined, out-of-order, superscalar* execution engine wearing the costume of one. Understanding the costume — but knowing the engine is underneath — is what separates "wrote some Parallel.For" from "tuned a hot path".

## The pipeline (Skylake-era x86 as the reference)

```
  ┌─────────────┐  ┌────────────┐  ┌────────────┐  ┌──────────────┐  ┌──────────┐  ┌────────┐
  │  Fetch      │→ │  Decode    │→ │  Rename    │→ │  Schedule /  │→ │  Execute │→ │ Retire │
  │  (16 B/cyc) │  │  → µops    │  │  (256 regs)│  │  Issue (8 ports)│ │ (ALUs/  │  │ (in    │
  │             │  │            │  │            │  │              │  │  FPU/   │  │  order)│
  │             │  │            │  │            │  │              │  │  AGU)   │  │        │
  └─────────────┘  └────────────┘  └────────────┘  └──────────────┘  └──────────┘  └────────┘
        ↑                                                                                ↓
   branch predictor                                                              store buffer / LSU
```

Important consequences for concurrency:

1. **Out-of-order execution** retires up to ~4–6 instructions per cycle in steady state, **as long as it can find independent work**. Tight dependent chains (e.g., `x = f(x); x = g(x); x = h(x);`) defeat this. So do unpredictable branches.
2. **Speculation** means the CPU is constantly executing past unresolved branches and rolling back if wrong. A single mispredict on a hot loop is ~15–20 cycles of waste.
3. **Store buffer** holds retired stores until they drain to L1. Loads on the *same* core can forward from the store buffer. Loads on a *different* core cannot — so writes appear delayed to other cores. This is the engine of x86's TSO model.

## Implications for .NET code

| If you write… | Hardware sees… | Implication |
|---|---|---|
| `for (int i; i<N; i++) sum += a[i] * b[i];` | Heavily pipelinable, dense ALU | The JIT will autovectorise on .NET 8+ where it can; expect ≥4× over the scalar version when memory-bound disappears. |
| Polymorphic call site (`virtual` / interface) | Indirect branch | Mispredict cost. JIT's tier-1 dynamic PGO devirtualises the hot dispatch in .NET 8+. |
| Tight `Interlocked.Increment(ref shared)` from many threads | Repeated RFOs on one cache line | Throughput collapses; see [Cache-Coherency](../Cache-Coherency/). |
| Branchy loop with random predicate | Mispredicts everywhere | Consider branchless tricks (`bool` → `int` arithmetic) when measured to help. |

## Reading disassembly in .NET

Three tools you should be able to use without thinking:

```bash
# 1. dotnet's built-in disasm dump (TieredCompilation=0 to get final code only).
DOTNET_JitDisasm="MyMethod" dotnet run -c Release

# 2. BenchmarkDotNet's [DisassemblyDiagnoser].
[DisassemblyDiagnoser(maxDepth: 2, printSource: true)]
public class Bench { /* ... */ }

# 3. SharpLab.io for one-off snippets (browser; no install).
```

When tuning, start with the disassembly. If you can't read it, you are guessing.

## Further reading

- Agner Fog — *Optimizing software in C++* and the *Microarchitecture* manual (free, definitive).
- Intel Optimization Reference Manual.
- *What Every Programmer Should Know About Memory*, Ulrich Drepper, 2007 (free; still essential).
