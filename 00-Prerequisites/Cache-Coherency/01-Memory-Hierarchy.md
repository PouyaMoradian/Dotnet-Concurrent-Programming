# The memory hierarchy

A CPU isn't fast because it adds quickly. A CPU is fast because the *data was already where it needed it to be*. The hierarchy that puts the data there — registers, L1, L2, L3, DRAM — is the single most important determinant of real-world performance, and the part most code never thinks about.

## The full ladder

Typical numbers for a 4 GHz x86-64 core in 2024–2026 (Intel Sapphire Rapids / AMD Zen 4 / Apple Silicon are within a small factor):

| Level | Where it lives | Capacity (per core unless noted) | Latency | Bandwidth |
|---|---|---|---|---|
| Registers | inside the core | 16–32 × 64-bit GP (general-purpose), 32 × 128–512-bit SIMD (vector) | 0 cycles | ~1 op/cycle |
| L1d (data) | inside the core | 32–48 KB (Intel 48, AMD Zen 32), 128 KB on Apple P-cores | 4–5 cycles (~1 ns) | ~200 GB/s |
| L1i (instructions) | inside the core | 32 KB | 4–5 cycles | ~200 GB/s |
| L2 | inside the core | 512 KB – 2 MB (4 MB on Apple) | 12–14 cycles (~3 ns) | ~100 GB/s |
| L3 (LLC — Last-Level Cache) | shared on the die | 8 MB – 256 MB | 40–50 cycles (~10–15 ns) | ~50 GB/s |
| Local DRAM | DIMM on same socket | 16 GB – 6 TB | 250–400 cycles (~80–100 ns) | 30–80 GB/s per channel |
| Remote DRAM | DIMM on other socket | as above | 500–800 cycles (~150–200 ns) | constrained by **UPI** / **IF** (Intel Ultra Path Interconnect / AMD Infinity Fabric) |
| NVMe SSD | PCIe | 1 TB – 30 TB | ~50–100 µs | 5–14 GB/s |
| Network (datacenter RTT) | NIC + switch | unlimited | 100 µs – 1 ms | 25–400 Gb/s |

Each step is ~3–4× slower than the one above. **The hierarchy is exponential, and almost every performance question is about which level your data lives in.**

## Why levels at all?

Two physical realities:

1. **SRAM is fast but expensive.** A megabyte of L1 SRAM is roughly 10× the silicon cost of a megabyte of DRAM. Cheap memory is far away; close memory is small.
2. **Speed-of-light delay is real.** Even at the speed of light, signalling between dies on a multi-die package can take ~5 ns. Across a socket boundary, it's tens of ns.

The hierarchy is the engineering compromise: keep what's hot in the small/fast tier, demote when no longer hot, fetch on demand.

## Cache lines: the unit everything moves in

The cache doesn't move bytes. It moves **lines** — 64 bytes on x86 and most ARM, **128 bytes on Apple Silicon**. When you read one byte from DRAM, the cache pulls the whole 64-byte line containing it. The next byte's read is then free (L1 hit).

This is why sequential access is fast and random access is slow even at the same total byte count. It's also why **false sharing** is a bug: two threads writing to "different" variables that happen to share a 64-byte region serialise.

To see your machine's effective line size, run demo 0 (`CacheLineProbe`).

## Cache organisation: associativity and indexing

A cache isn't a flat array of lines. It's broken into **sets**; an address's middle bits choose a set, and each set has K ways (a "K-way set associative" cache). A line lives in any of the K ways of its set, and you get a hit if it's in any of them.

| Cache | Typical organisation |
|---|---|
| L1d | 8-way set associative, 32–48 KB |
| L2 | 8–16-way set associative, 1 MB |
| L3 | 16–20-way set associative, slice-hashed across cores |

Set-associativity creates a subtle gotcha: a power-of-two stride that lands on the same set repeatedly can fill the K ways and evict your hot data even though you've only touched a few KB. Real-world example: a 2D array with a power-of-two width walked column-major. The Wikipedia article on cache conflict misses has the canonical example.

## Hardware prefetching

Each L1 has one or more **prefetchers** that observe your access pattern and pull in lines before you ask. The two common kinds:

1. **Stride prefetcher.** Detects "access X, X + s, X + 2s" and starts pulling X + 3s, X + 4s, … This is why a tight `for` loop with a fixed stride feels free past the first iteration.
2. **Adjacent-line prefetcher.** Pulls the *next* line every time you touch one. So sequential access is even faster than the stride prefetcher alone implies.

You will rarely need to think about prefetching directly. When you do, .NET 8+ exposes `System.Runtime.Intrinsics.X86.Sse.Prefetch0/1/2` and ARM equivalents. The honest advice: try to *enable* the hardware prefetcher with predictable strides first; reach for software prefetch only when measurement proves the hardware one isn't doing the job.

## Translation Lookaside Buffer (TLB)

Every memory access is a virtual address. The CPU translates it to a physical address through page tables. The **TLB** caches recent translations. Sizes:

| TLB level | Entries | Coverage at 4 KB pages |
|---|---|---|
| L1 dTLB | 64–128 | 256–512 KB |
| L1 iTLB | 64–128 | 256–512 KB |
| L2 STLB (shared) | 1024–2048 | 4–8 MB |

A TLB miss costs another memory access (the page walker reads page-table entries from cache or DRAM). Two consequences:

1. **A loop over many small allocations may TLB-thrash.** Working set in *bytes* might fit in L3, but the *number of pages* spans tens of thousands of TLB entries.
2. **Large pages (2 MB or 1 GB) increase TLB reach.** .NET supports this on Linux via `<LargePages>true</LargePages>` in runtime config / env var; on Windows via `MEM_LARGE_PAGES`. Use sparingly — they're a heavy-hammer optimisation.

## Working-set rules of thumb

| Workload size | Lives in | Loop throughput rough |
|---|---|---|
| < 32 KB | L1 | Memory-irrelevant; CPU-bound |
| 32 KB – 1 MB | L2 | Still very fast |
| 1 MB – 32 MB | L3 | Noticeably slower; still single-digit ns/op |
| > 32 MB | DRAM | Bandwidth-bound; you're now limited by GB/s |

When tuning a hot path, ask **"what level does my working set live in?"** before reaching for any optimisation. A 100 KB hash table is fundamentally a different beast from a 100 MB one even if the algorithm is identical.

## Practical takeaways

- **Sequential access is ~10× faster than random** at the same total bytes — even on DRAM, because the prefetcher hides latency.
- **Pack hot data tightly.** Two `long` fields adjacent in a struct share a line; one referenced field and ten unreferenced ones waste your cache.
- **AoS vs SoA** (Array of Structures vs Structure of Arrays)**.** For hot loops that touch one field per object across many objects, *Structure of Arrays* beats *Array of Structures*. The classic example: position updates in a game's **ECS** (Entity-Component-System framework).
- **Watch your row stride.** Power-of-two widths on 2D arrays cause set conflicts; pad to a non-power-of-two if you see weird perf cliffs.

## Lab

```bash
dotnet run --project 00-Prerequisites -- 6
```

`MemoryLatencyLadderDemo` walks arrays of doubling sizes from 4 KB to 256 MB and reports ns/access. Look for the *steps*: ns/access jumps when the working set crosses L1, L2, L3, and DRAM boundaries. The step locations tell you your machine's cache sizes.

## Further reading

- **Ulrich Drepper — *What Every Programmer Should Know About Memory*** — the chapter on the cache hierarchy is the canonical introduction.
- **Igor Ostrovsky — *Gallery of Processor Cache Effects*** — eight short experiments that make the hierarchy concrete.
- **Intel optimisation manual**, sections on cache architecture and prefetching.
