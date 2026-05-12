# Topology and latencies

NUMA is the name for any system where the latency or bandwidth of a memory access depends on *where* in the machine the requesting core lives. This file is the orientation: what the topology looks like on modern machines, what each hop costs, and how to read a NUMA distance matrix.

## The canonical 2-socket diagram

```
            ┌──────────────────────────┐                ┌──────────────────────────┐
            │  Socket 0  (Node 0)      │                │  Socket 1  (Node 1)      │
            │ ┌────┐┌────┐┌────┐┌────┐ │                │ ┌────┐┌────┐┌────┐┌────┐ │
            │ │C0 ││C1 ││C2 ││C3  │ │                  │ │C28││C29││C30││C31│ │
            │ └────┘└────┘└────┘└────┘ │                │ └────┘└────┘└────┘└────┘ │
            │     L1, L1, L1, L1       │                │     L1, L1, L1, L1       │
            │       L2, L2, L2, L2     │                │       L2, L2, L2, L2     │
            │         L3 (shared)      │                │         L3 (shared)      │
            │            │             │                │            │             │
            │       ┌─── DRAM ────┐    │ ◄─── UPI ─────►│       ┌─── DRAM ────┐    │
            │       │ 384 GB local│    │   (~30-80 GB/s)│       │ 384 GB local│    │
            │       └─────────────┘    │                │       └─────────────┘    │
            └──────────────────────────┘                └──────────────────────────┘
```

A read from C0 to its socket's DRAM is *local*. A read from C0 to socket 1's DRAM hops the **UPI** (Ultra Path Interconnect) link on Intel or the **Infinity Fabric** link on AMD. The hop adds ~60–100 ns to the read.

## The 4-socket diagram, with hop counts

```
        Node 0 ────────── Node 1
          │                │
          │                │
        Node 2 ────────── Node 3
```

If you're on node 0 and need data from node 3, you might go via node 1 or node 2 (2 hops). The "distance matrix" the OS keeps shows this — typical values:

```
node:    0    1    2    3
   0:   10   16   16   30
   1:   16   10   30   16
   2:   16   30   10   16
   3:   30   16   16   10
```

The numbers are relative latencies (10 = local, 30 = two hops). On Linux: `numactl --hardware` prints exactly this matrix. The OS scheduler reads it and tries to keep threads near their memory.

## AMD EPYC chiplets and "NPS modes"

AMD's EPYC processors are not single dies; they're an I/O die plus 4–12 **CCDs** (Core Complex Dies — the per-chiplet compute slices). Each CCD has 8 cores and a local L3. The memory controllers live on the I/O die, but access from a CCD to a memory channel can be closer or farther.

EPYC **BIOS** (Basic Input/Output System — the platform firmware) exposes **NPS** (NUMA Per Socket) modes:

| Mode | NUMA nodes per socket | Use |
|---|---|---|
| NPS=1 | 1 | Simplest; the OS sees a flat memory space. Best for code that doesn't NUMA-tune. |
| NPS=2 | 2 | Split socket into halves. Compromise. |
| NPS=4 | 4 | Each pair of CCDs is one node. Best for NUMA-aware code; worst for naive code. |

On Genoa (4th-gen EPYC) NPS=4 routes memory through ~1/4 of channels per node — local channel BW is 8 channels / 4 nodes = 2 channels each, but cross-channel hopping is *much* faster than cross-socket. Mostly relevant if you're tuning specific server workloads.

## Apple Silicon, NVIDIA Grace, ARM

Three modern systems that look "non-NUMA" but have NUMA-like effects:

- **Apple M2 Ultra / M3 Max** glue two chips via UltraFusion. The OS sometimes exposes this as a unified node; memory access patterns *can* show ~10–20% asymmetry across the seam in some workloads.
- **NVIDIA Grace-Hopper** has CPU memory (**LPDDR5X** — low-power DDR5) and GPU memory (**HBM** — High-Bandwidth Memory, the stacked-DRAM packages on accelerators) coherent across **NVLink-C2C** (chip-to-chip). Effectively a 2-domain NUMA where one domain is "GPU memory".
- **ARM Neoverse N2/V2 in multi-socket** — same UPI / IF-equivalent story. Linux's `numactl` works there too.

## How to read the topology from your code

There is no first-class .NET API. The cross-platform pragmatic options:

```csharp
// Linux: read /sys.
var nodes = Directory.GetDirectories("/sys/devices/system/node", "node*");
foreach (var node in nodes)
{
    var cpus = File.ReadAllText(Path.Combine(node, "cpulist")).Trim();
    Console.WriteLine($"{Path.GetFileName(node)}: {cpus}");
}

// Windows: P/Invoke GetNumaHighestNodeNumber + GetNumaNodeProcessorMaskEx.
```

Or just shell out to `numactl --hardware` / `lscpu --extended` and parse — usually fine for tooling.

## Bandwidth, not just latency

Latency is the easy story. Bandwidth often hurts more:

| Cross-socket path | Aggregate bandwidth |
|---|---|
| Intel UPI 2.0 (Sapphire Rapids), 2 links | ~80 GB/s |
| AMD Infinity Fabric (Genoa), 4 IF links | ~100 GB/s |
| NVLink C2C (Grace-Hopper) | 900 GB/s (this is the *good* case) |

Compare with local DRAM bandwidth — ~300 GB/s for a fully populated 8-channel server. A single cross-socket bandwidth-bound thread can pull *all* of UPI to itself and starve everyone else. The classic horror story: a benchmark that streams a 64 GB array; node-local it sustains 280 GB/s, node-remote it caps at 80 GB/s and saturates the interconnect for every other process.

## Implications you'll meet in later chapters

- **Server GC creates a heap per logical CPU.** On NUMA, .NET 8+ pins those heaps node-locally (mostly). See [03-DotNet-Server-GC-on-NUMA.md](03-DotNet-Server-GC-on-NUMA.md).
- **ConcurrentDictionary's reads on a NUMA box** may pull data from the wrong node if the bucket was first allocated by a remote-node thread. Sharding helps.
- **ArrayPool.Shared on a NUMA box** is a hazard — a buffer rented on node 0 may be returned, re-rented, and used on node 1. See [05-ArrayPool-and-Buffers.md](05-ArrayPool-and-Buffers.md).

## Practical takeaways

- NUMA topology is a property of the *machine*, not of the .NET runtime. Find it, treat it as a constraint.
- The cost of "remote" is ~1.5–2× on latency, plus bandwidth caps.
- Most laptops and many cloud VMs are single-node — NUMA is a no-op.
- On true multi-node hardware, the right design is to *partition by node*: per-node workers, per-node buffer pools, per-node aggregates.

## Lab

```bash
# Inspect topology:
numactl --hardware
lscpu --extended
```

```bash
# Run LocalityDemo unbound, then bound to one node:
dotnet run --project 00-Prerequisites -- 3
numactl --cpunodebind=0 --membind=0 dotnet run --project 00-Prerequisites -- 3
```

The bound run should show lower variance and (on a multi-node host) a lower read phase time.

## Further reading

- **Christoph Lameter — *NUMA (Non-Uniform Memory Access): An Overview*** (free) — 30-page primer.
- **AMD — *Infinity Architecture White Paper***.
- **Intel — *Sapphire Rapids platform brief***.
- **`numactl(8)`** and **`man 7 numa`** — Linux user-space NUMA APIs.
