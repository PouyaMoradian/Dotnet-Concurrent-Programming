# CPU architecture for .NET developers — overview

A modern x86-64 / ARM64 CPU is not a deterministic instruction-fetch machine; it is a *speculative, pipelined, out-of-order, superscalar* execution engine wearing the costume of one. Understanding the costume — but knowing the engine is underneath — is what separates "wrote some `Parallel.For`" from "tuned a hot path".

This section unpacks the parts of the engine that surface in concurrent .NET code: the pipeline, branch prediction, the wide vector units, the store buffer that makes x86's memory model what it is, and the tooling to see what the JIT actually emitted.

## What's in this section

| File | What it covers |
|---|---|
| [01-Pipeline-and-OoO.md](01-Pipeline-and-OoO.md) | The fetch/decode/rename/issue/execute/retire pipeline; **ILP** (instruction-level parallelism); what "superscalar" buys you; why dependent chains are slow |
| [02-Branch-Prediction.md](02-Branch-Prediction.md) | How the predictor works, the cost of a mispredict, sorted-vs-unsorted, branchless coding tricks, devirtualisation in .NET 8+ |
| [03-SIMD-and-Vector-Units.md](03-SIMD-and-Vector-Units.md) | **SIMD** (Single Instruction, Multiple Data) on x86 (SSE / AVX / AVX-512) and ARM (NEON / SVE — Scalable Vector Extension); `System.Numerics.Vector<T>`, `Vector256<T>`, hardware intrinsics; when autovectorisation kicks in |
| [04-Store-Buffer-and-Memory-Ordering.md](04-Store-Buffer-and-Memory-Ordering.md) | The store buffer; x86 **TSO** (Total Store Order) vs ARM's weak model; how this turns into the rules in `Volatile.Read`/`Volatile.Write` |
| [05-Reading-JIT-Disassembly.md](05-Reading-JIT-Disassembly.md) | Three practical tools to see machine code from C# — `DOTNET_JitDisasm`, BenchmarkDotNet's disassembly diagnoser, SharpLab |

## The 60-second summary

```
  ┌─────────┐  ┌────────┐  ┌─────────┐  ┌──────────────┐  ┌─────────┐  ┌────────┐
  │  Fetch  │→ │ Decode │→ │  Rename │→ │ Schedule /   │→ │ Execute │→ │ Retire │
  │ 16 B/cyc│  │ → µops │  │ 256 regs│  │ Issue (8     │  │ (ALUs/  │  │  (in   │
  │         │  │        │  │         │  │  ports)      │  │  FPU/   │  │  order)│
  │         │  │        │  │         │  │              │  │  AGU)   │  │        │
  └─────────┘  └────────┘  └─────────┘  └──────────────┘  └─────────┘  └────────┘
       ↑                                                                     ↓
  branch predictor                                                   store buffer / LSU
```

(ALU = Arithmetic Logic Unit; FPU = Floating-Point Unit; AGU = Address Generation Unit; LSU = Load/Store Unit.)

The implications that travel up to your C# code:

1. **The pipeline is hungry for independent work.** Tight dependent chains (`x = f(x); x = g(x); x = h(x);`) stall it. Loops with multiple independent accumulators feed it. See [01-Pipeline-and-OoO.md](01-Pipeline-and-OoO.md).
2. **Unpredictable branches are expensive.** ~15–20 cycles per mispredict on a 5+ GHz core. A branchless rewrite can win ~3–6× on hot code. See [02-Branch-Prediction.md](02-Branch-Prediction.md).
3. **Vector units widen every instruction.** A single AVX2 ADD does 8 floats at once. .NET 8+ autovectorises many simple loops. See [03-SIMD-and-Vector-Units.md](03-SIMD-and-Vector-Units.md).
4. **Stores don't become globally visible the instant they retire.** They sit in the store buffer first. That's why x86's "almost sequential" memory model still allows store-load reordering. See [04-Store-Buffer-and-Memory-Ordering.md](04-Store-Buffer-and-Memory-Ordering.md).
5. **You can read what the JIT emitted in 30 seconds.** No excuse to guess. See [05-Reading-JIT-Disassembly.md](05-Reading-JIT-Disassembly.md).

## What .NET gives you to exploit each

| Hardware feature | .NET surface area |
|---|---|
| ILP / OoO | The JIT schedules µops reasonably; you mostly enable it by writing loops with multiple accumulators. |
| Branch prediction | `[MethodImpl(MethodImplOptions.AggressiveInlining)]`; PGO devirtualises hot virtual calls on tier-1 |
| SIMD | `System.Numerics.Vector<T>` (portable), `Vector128/256/512<T>` (sized), `System.Runtime.Intrinsics.X86/Arm` (raw) |
| Store buffer / memory order | `Volatile.Read`/`Write`, `Interlocked`, `MemoryBarrier`, `[Volatile]` field |
| Disassembly | `DOTNET_JitDisasm=MethodName`, `[DisassemblyDiagnoser]`, SharpLab.io |

## Demos in this chapter that exercise this section

- **`BranchPredictionDemo`** (demo 4) — sorted-vs-unsorted array sum.
- **`InstructionLevelParallelismDemo`** (demo 5) — single vs four accumulators.
- **`MemoryLatencyLadderDemo`** (demo 6) — exposes how the pipeline stalls when memory misses.
- **`SimdSpeedupDemo`** (demo 9) — scalar vs `Vector<T>` sum.

## Further reading

- **Agner Fog** — *Optimizing software in C++* + *The microarchitecture of Intel, AMD and VIA CPUs* (free; the definitive references).
- **Intel** — *Intel® 64 and IA-32 Architectures Optimization Reference Manual*.
- **ARM** — *Arm® Cortex® Software Optimization Guides* (per core, e.g. Neoverse N2).
- **Ulrich Drepper** — *What Every Programmer Should Know About Memory* (2007; still essential).
- **Daniel Lemire** — blog posts and papers on practical SIMD, branch prediction, and microbenchmarking.
