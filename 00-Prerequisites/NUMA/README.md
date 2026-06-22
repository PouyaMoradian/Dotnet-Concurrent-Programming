# NUMA — Non-Uniform Memory Access — overview

On a multi-socket server (and on chiplet AMD parts that expose multiple NUMA "domains" within one socket), not all RAM is equal. Memory is physically attached to a particular socket — *node* in NUMA-speak. Reads from a thread running on a *different* node cross a high-speed interconnect (**UPI** — Ultra Path Interconnect — on Intel, **Infinity Fabric** on AMD, **NVLink** on Grace-Hopper) and pay a latency penalty of roughly 1.5–2× on every miss. On bandwidth-bound workloads it can be much worse — you saturate the interconnect, not your DRAM channels.

This section is the practical guide: what NUMA topology looks like, where memory ends up by default, what .NET's Server GC does about it, and how to control placement when you need to.

## What's in this section

| File | What it covers |
|---|---|
| [01-Topology-and-Latencies.md](01-Topology-and-Latencies.md) | Sockets, chiplets, **CCDs** (Core Complex Dies — AMD's chiplets), mesh / UPI / Infinity Fabric, latency tables, hop counts |
| [02-First-Touch-Policy.md](02-First-Touch-Policy.md) | Linux and Windows default placement; the implications for allocator design |
| [03-DotNet-Server-GC-on-NUMA.md](03-DotNet-Server-GC-on-NUMA.md) | Per-heap allocation, GC awareness, and what .NET 8+ added |
| [04-Controlling-Placement.md](04-Controlling-Placement.md) | `numactl`, `ProcessorAffinity`, Group affinity, BCL APIs |
| [05-ArrayPool-and-Buffers.md](05-ArrayPool-and-Buffers.md) | Why `ArrayPool<T>.Shared` can be a NUMA antipattern; sharded buffer pools |

## The 60-second summary

Latencies, approximate, modern dual-socket Xeon or EPYC:

| Access | Latency | Bandwidth ceiling |
|---|---|---|
| L1 hit | 1 ns | hundreds of GB/s/core |
| L3 hit | ~12 ns | tens of GB/s shared |
| Local DRAM | ~80 ns | ~50 GB/s per channel × ~8 channels = 200–400 GB/s |
| Remote DRAM (1 hop) | ~150 ns | constrained by UPI/IF (~30–80 GB/s aggregate) |
| Remote DRAM (2 hops, mega-systems) | 200+ ns | further constrained |

Two consequences that drive design:

1. **Allocate where you consume.** A buffer first written by thread T on node 0 pins to node 0 (first-touch policy). If thread U on node 1 then reads it heavily, every access is remote.
2. **Distribute work along node boundaries.** Pin worker threads to nodes; spread the workload across nodes; let each node's workers reach into local memory only.

## How NUMA hides in modern hardware

The "multi-socket server" model is one form of NUMA, but it's not the only one:

- **AMD EPYC with multiple CCDs.** Each chiplet is a NUMA-ish domain even within one physical socket. Genoa exposes multiple NUMA nodes from a single socket via "**NPS**=4" (NUMA Per Socket) **BIOS** (Basic Input/Output System — the firmware that initialises the platform before the OS boots) mode.
- **Apple Silicon "M2 Ultra / M3 Max"** uses UltraFusion to glue two chips; access patterns benefit from awareness.
- **Cloud VMs** sometimes hide NUMA from the guest, sometimes don't. Bare-metal and full-socket sizes (e.g. AWS `m6i.metal` / `m6i.32xlarge`, both spanning the host's two Ice Lake sockets) typically expose 2 NUMA nodes; smaller shared sizes often present a single flattened node. Always confirm with `lstopo` / `numactl --hardware` on the actual instance. Azure has both flavours.

You won't always know your topology without checking. Use:

- **Linux:** `lscpu --extended` shows the NUMA columns; `numactl --hardware` gives the matrix.
- **Windows:** `Get-CimInstance Win32_NumaNode`.
- **.NET:** there's no first-class API. The Linux path is reading `/sys/devices/system/node/*`.

## When NUMA matters and when it doesn't

NUMA matters when:

- The host is multi-socket or multi-NUMA-node.
- Threads do significant memory access (not just CPU on registers).
- The workload isn't already cache-resident.

NUMA doesn't matter much when:

- The host has one node (most laptops and many cloud VMs).
- Working set fits in cache (L3 is socket-shared on Intel, mostly).
- Bandwidth ceilings aren't binding (low-throughput services).

The wrong reaction to "we have a NUMA box" is to immediately reach for `numactl`. The right reaction is to *measure* whether memory accesses are crossing nodes and *whether they cost throughput*.

## Demos in this chapter that exercise this section

- **`LocalityDemo`** (demo 3) — first-touch + rotated-thread reads; the variance comes from NUMA placement on multi-node hosts.
- **`MemoryLatencyLadderDemo`** (demo 6) — at the largest sizes, the latency floor depends on which node served the read.

## Further reading

- **Christoph Lameter — *NUMA (Non-Uniform Memory Access): An Overview*** — accessible primer.
- **Intel — *Intel® QuickPath Interconnect Architecture Reference***.
- **AMD — *Infinity Fabric Architecture***.
- **`numactl(8)` man page** — the user-space contract.
- **Maoni Stephens (`maoni0.medium.com`)** — .NET GC implementer; posts on Server GC and NUMA.
