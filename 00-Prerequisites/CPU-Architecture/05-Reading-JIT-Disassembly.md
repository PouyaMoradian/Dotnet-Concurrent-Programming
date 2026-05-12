# Reading JIT disassembly

If you can't see what the JIT actually emitted, you're guessing. This file gets you to the assembly in three different ways, picks one for each situation, and walks through reading the output without becoming an x86 manual reader.

## Why look at disassembly at all?

Three questions you can't answer otherwise:

1. **Did the JIT vectorise this?** "Should have" isn't proof; `vaddps`/`vpaddd` in the listing is.
2. **Did the JIT inline this small helper, or pay the call cost?** Inlining decisions are driven by IL size, attributes, and tier; the listing settles it.
3. **Did the JIT remove the bounds check?** A `cmp/jae`-or-similar pair before each indexed load means *no*. A clean indexed load means yes.

You don't need to read every line. You need to *find the loop body* and look for what's there and what isn't.

## Tool 1 — `DOTNET_JitDisasm` (no install, no benchmark needed)

The .NET runtime can dump the JIT's output to stdout. Set `DOTNET_JitDisasm` to a method-name glob:

```bash
# Linux/macOS
DOTNET_JitDisasm='*HotMethod*' \
DOTNET_TieredCompilation=0      \
  dotnet run -c Release --project 00-Prerequisites
```

```powershell
# Windows
$env:DOTNET_JitDisasm = '*HotMethod*'
$env:DOTNET_TieredCompilation = '0'
dotnet run -c Release --project 00-Prerequisites
```

- `DOTNET_TieredCompilation=0` forces final (tier-1) compilation at first call, so you see the optimised code, not the throwaway tier-0 version.
- Wildcards on names work; quote them to avoid shell globbing.
- Output goes to *stdout* mixed in with your program's output. Redirect to a file if it's noisy: `... > disasm.txt`.

Other helpful knobs in the same family:

| Env var | What it does |
|---|---|
| `DOTNET_JitDisasm` | Names of methods to disassemble. |
| `DOTNET_JitDisasmAssemblies` | Restrict to certain assemblies. |
| `DOTNET_JitDiffableDasm=1` | Stable formatting; useful for diffing two builds. |
| `DOTNET_JitDisasmSummary=1` | Just the method headers, not the bodies. |
| `DOTNET_JitDump=*Method*` | Dump *internal* JIT IR phases (verbose; for compiler hackers). |

Use this when you want the answer in 30 seconds and you don't care about timing.

## Tool 2 — BenchmarkDotNet's `[DisassemblyDiagnoser]`

When you're already benchmarking, hang the diagnoser off the benchmark class:

```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Diagnosers;

[DisassemblyDiagnoser(maxDepth: 2, printSource: true, exportHtml: true)]
[MemoryDiagnoser]
public class SumBench
{
    private int[] _data = Enumerable.Range(0, 100_000).ToArray();

    [Benchmark]
    public long Naive()
    {
        long s = 0;
        for (int i = 0; i < _data.Length; i++) s += _data[i];
        return s;
    }
}

class Program
{
    static void Main(string[] args) => BenchmarkRunner.Run<SumBench>();
}
```

- `maxDepth: 2` follows one level of inlinees so you see what the JIT actually inlined.
- `printSource: true` interleaves the C# source line with each asm chunk — invaluable.
- `exportHtml: true` writes a clickable HTML report.

BenchmarkDotNet also has `[HardwareCounters]` and `[InliningDiagnoser]`. Stack them when you're chasing a performance regression.

Use this when *timing matters*: you want to verify a perf claim *and* see the code.

## Tool 3 — SharpLab.io

Browser. No install. Paste C# on the left; pick "JIT Asm" on the right; press run. Instant.

Use this for *one-off* questions: "does the JIT inline this?", "what does `Span<int>.IndexOf` lower to?", "how is a pattern match compiled?". Don't use it for serious benchmarking — you're on a public free instance and you're seeing whatever runtime SharpLab is currently configured for.

## Reading the output without becoming an asm expert

Below is a stripped sample from `DOTNET_JitDisasm='SumBench.Naive'`:

```text
; Assembly listing for method SumBench.Naive():long
G_M000_IG01:
       push     rdi
       push     rsi
       push     rbx

G_M000_IG02:
       mov      rdi, rcx                  ; rcx = this; save to rdi
       mov      rsi, gword ptr [rdi+8]    ; load _data field
       mov      ebx, dword ptr [rsi+8]    ; ebx = _data.Length
       xor      ecx, ecx                  ; sum = 0
       xor      eax, eax                  ; i = 0

G_M000_IG03:
       cmp      eax, ebx
       jge      SHORT G_M000_IG05

G_M000_IG04:
       movsxd   rdx, eax                  ; rdx = i (sign-extend to 64-bit)
       mov      edx, dword ptr [rsi+4*rdx+16]   ; _data[i]
       movsxd   rdx, edx
       add      rcx, rdx                  ; sum += _data[i]
       inc      eax                       ; i++
       cmp      eax, ebx
       jl       SHORT G_M000_IG04         ; loop if i < Length

G_M000_IG05:
       mov      rax, rcx                  ; return sum
       pop      rbx
       pop      rsi
       pop      rdi
       ret
```

What to notice in 60 seconds:

1. **The hot loop is `G_M000_IG04`.** Three real ops: load, add, increment. One `cmp`/`jl`. That's a tight, dense scalar loop.
2. **No bounds check inside the loop.** The JIT proved `i < Length`. If you saw `cmp eax, ebx; jae G_M000_IGSomeThrow`, the bounds check was *kept*.
3. **No vectorisation.** No `vmovdqu`, no `vpaddd`. Either the JIT decided the loop wasn't worth vectorising, or it couldn't prove it safe to. This is your cue to look at `Vector<T>`.

If you saw something like:

```text
       vmovdqu  ymm0, ymmword ptr [rsi+4*rdx+16]
       vpaddd   ymm1, ymm1, ymm0
       add      eax, 8
```

…then the JIT *did* vectorise; one 256-bit (8 × `int`) load + vector add per loop iteration.

## A short cheatsheet of "what to look for"

| Symptom in asm | Meaning |
|---|---|
| `vaddps`, `vpaddd`, `vmovdqu`, `ymm`/`zmm` regs | SIMD; loop is vectorised |
| `cmov` | Branchless conditional move |
| `call` to a method you expected inlined | Inlining didn't happen — check IL size and `[MethodImpl]` |
| `cmp / jae / call HelperThrow` before each indexed load | Bounds check kept |
| `lock cmpxchg`, `xadd` | `Interlocked.*` |
| `mfence` | Full memory barrier |
| `xchg [mem], reg` | An implicit fence (every locked op on x86 is one) |
| `pause` | Spin-wait hint (used by `SpinWait`) |

## When *not* to read the assembly

Most of the time. The JIT is good. Most concurrency bugs are not micro-optimisation problems; they're architecture problems. Read the assembly when:

- You have a benchmark and a hypothesis about what should be faster, and you want to confirm what the JIT did.
- You're surprised by a `[HardwareCounters]` reading (e.g., huge branch-miss rate; many cache misses).
- You're writing the inner loop of a hot library (parser, allocator, compression). For these, every byte of code and every memory access counts.

## Practical takeaways

- The JIT can do more than most developers expect: bounds-check elimination, devirtualisation, vectorisation, escape analysis (limited).
- It still can't do magic: it won't restructure your algorithm; it won't make a memory-bandwidth-bound loop go faster; it won't vectorise across an unproven aliasing.
- Reading the assembly *once* on a hot path is a 10-minute investment that often saves hours of guessing.

## Further reading

- **`/.dotnet/runtime/docs/design/coreclr/jit/viewing-jit-dumps.md`** — the official inventory of every `DOTNET_Jit*` switch.
- **BenchmarkDotNet docs** — the `[DisassemblyDiagnoser]` page is a tiny page; read it once.
- **EgorBo's blog** — many posts walking through how a specific C# construct gets compiled in modern .NET.
- **Konrad Kokosa — *Pro .NET Memory Management*** (book) — best treatment of how to *interpret* what you see in the dump.
