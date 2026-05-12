# First-touch placement

When your program does `new byte[1_000_000]`, the .NET runtime asks the OS for a virtual address range. The OS *reserves* the range; it doesn't yet attach physical RAM. Physical RAM is allocated lazily, page by page, the *first time someone writes* to each page. This is called **first-touch** placement, and it has profound consequences on NUMA systems.

## What the OS does at allocation time

```csharp
var buf = new byte[1_000_000];   // reserves 1M of virtual address, zero physical pages allocated
```

Behind the scenes on Linux:
1. `mmap(...)` returns a virtual range mapped to anonymous private memory.
2. The pages are marked "demand-paged" — the page-table entries point to a system-wide "zero page" or are not present at all.
3. Nothing physical is allocated yet.

When *any* thread first writes to a byte at offset N:
1. The CPU traps on the not-present (or **COW**-of-zero-page — Copy-On-Write: the page is shared read-only until written, at which point a private copy is made) entry.
2. The OS page fault handler runs on that core.
3. It allocates a physical page **from the NUMA node where the current thread is executing**.
4. The page table is updated to map virtual address → that physical page.
5. The faulting instruction retries.

Now that page is pinned to that node. Future accesses from *any* thread on the same node are local; accesses from threads on other nodes are remote.

## Why this matters

Imagine you have a producer thread on node 0 building a large buffer, and a consumer thread pool on node 1 reading it. The "first-touch" thread is the producer, so every page lands on node 0. The consumers pay the remote penalty on every line they read.

Or worse — a "build it lazily" pattern where the constructor of a large object allocates and zeros memory on the constructing thread, then the object is handed to another thread for use. The construction thread's node owns every page.

The same problem in reverse: an array passed around between thread pool workers, with each task touching different regions, ends up with pages spread across whichever nodes the workers happened to be on at first-touch time. A subsequent linear scan will read pages from all nodes — fast on average, but with no locality at all.

## Linux variants

The kernel exposes alternative policies via `set_mempolicy` / `mbind`:

| Policy | Behaviour |
|---|---|
| `MPOL_DEFAULT` | First-touch (the default). |
| `MPOL_BIND` | Allocate only from a specified node set; fault if exhausted. |
| `MPOL_INTERLEAVE` | Round-robin pages across a node set — useful for big shared arrays. |
| `MPOL_PREFERRED` | Try the specified node first; fall back to any. |

`numactl` exposes these on the command line:

```bash
numactl --interleave=all dotnet run    # spread all pages round-robin
numactl --membind=0     dotnet run    # only use node 0's DRAM
numactl --preferred=0   dotnet run    # prefer node 0 but fall back
```

For a workload that does big sequential scans and runs threads everywhere, `--interleave=all` is often surprisingly good — it removes asymmetry by spreading uniformly.

## Windows

Windows has analogous mechanisms:

- `VirtualAllocExNuma` — request memory from a specific node.
- `SetThreadGroupAffinity` / `SetThreadIdealProcessor` — hint where the thread should run.
- The Win32 page-fault handler also uses first-touch by default.

## Transparent Huge Pages

On Linux, the kernel may transparently promote 512 consecutive 4 KB pages into a single 2 MB *huge page*. This reduces **TLB** (Translation Lookaside Buffer) pressure but tangles with NUMA: if the kernel promotes a region that contains pages from multiple nodes, it has to *move* pages (huge pages must be physically contiguous and same-node). The page-moving can show up as latency spikes.

For benchmark stability you might disable **THP** (Transparent Huge Pages) for a specific process:
```bash
echo never > /sys/kernel/mm/transparent_hugepage/enabled       # system-wide; needs root
# or per-process via prctl(PR_SET_THP_DISABLE) — typically not from .NET
```

For sustained latency-critical workloads you might *enable* explicit huge pages, see `madvise(MADV_HUGEPAGE)`.

## How this interacts with the .NET GC

The GC allocates large segments of virtual address space up-front; the *individual objects* you allocate become part of these segments. The first-touch policy applies *per page*, not per object. So:

- A GC heap segment first-touched by a worker thread on node 0 has its physical pages on node 0.
- Server GC's per-CPU heaps mostly first-touch on the CPU they're meant to serve, *iff* the worker stays affined.
- The "heap balancing" Server GC does on .NET 8+ moves work between heaps to balance load; first-touch behaviour on the new heap depends on where the moving thread runs.

The runtime team has been steadily improving NUMA awareness here; treat it as a moving target and *measure* on your specific .NET version.

## Practical takeaways

- Allocate from the thread that'll *consume* the data, not the one that builds it.
- For shared arrays that everyone reads, consider `numactl --interleave=all`.
- Buffer pools that hand a buffer to whichever thread asks (`ArrayPool<T>.Shared`) violate first-touch locality. See [05-ArrayPool-and-Buffers.md](05-ArrayPool-and-Buffers.md).
- Constructors that fill a large buffer should run on the consumer's node, or use interleaving.

## Lab

`LocalityDemo` (demo 3) first-touches each chunk from a `Parallel.For` worker, then reads from a *different* worker. On a multi-node host with default policy, the second phase is sensitive to where pages landed. Rerun with `numactl --interleave=all` and observe the spread (smaller variance, sometimes slightly slower mean — interleaving exchanges peak local bandwidth for predictable average).

## Further reading

- **`man 2 set_mempolicy`** and **`man 2 mbind`** — Linux NUMA APIs.
- **Christoph Lameter — *NUMA on Linux*** — practical guide.
- **Microsoft Docs — *NUMA support on Windows***.
