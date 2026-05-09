# NUMA — Non-Uniform Memory Access

On a multi-socket server (and on chiplet AMD parts that expose multiple NUMA "domains" within one socket), not all RAM is equal. Memory is physically attached to a particular socket. Reads from a thread running on a *different* socket cross a high-speed interconnect (UPI on Intel, Infinity Fabric on AMD) and pay a latency penalty:

| Access type | Approx latency |
|---|---|
| Local DRAM (same socket as the thread) | ~80 ns |
| Remote DRAM (one socket hop) | ~140–180 ns |
| Two-hop (very large systems) | 200+ ns |

It's a 1.5–2× cost on every miss. On bandwidth-bound workloads it can be much worse — you saturate the interconnect.

## How memory ends up where it does

Linux uses **first-touch**: the page is bound to the NUMA node of the thread that first writes to it. So `new byte[1_000_000]` allocates virtual memory; the *first* thread to write a byte at offset N pins that page to its node.

Windows has similar semantics; explicit control via `SetThreadIdealProcessor` + `VirtualAllocExNuma`.

## Implications for .NET

1. **Allocate on the consumer.** If thread T will read a buffer thousands of times, ideally T allocates it.
2. **Pin work to NUMA nodes for HFT-style hot paths.** The TPL doesn't do this for you. Use `ProcessorAffinity` or, on Linux, launch with `numactl --cpunodebind=0 --membind=0`.
3. **Server GC has per-heap structure.** With `<ServerGarbageCollection>true</ServerGarbageCollection>` (default in this repo's `Directory.Build.props`), the runtime creates one heap per logical CPU. On NUMA, .NET 8+ improved the pinning so that heaps tend to allocate node-locally.
4. **`ArrayPool<T>.Shared` is process-wide.** A buffer rented on socket 0 may be returned and re-rented on socket 1. For hot pipelines, consider per-node `ArrayPool<T>.Create()`.

## Detecting NUMA from .NET

`System.Diagnostics.Process` exposes `ProcessorAffinity` but not NUMA topology. Two pragmatic options:

```csharp
// Option A: read /sys on Linux
foreach (var nodeDir in Directory.GetDirectories("/sys/devices/system/node", "node*"))
    Console.WriteLine(nodeDir);

// Option B: ask Windows via P/Invoke
// GetNumaHighestNodeNumber, GetNumaNodeProcessorMask
```

If you write code that ships in containers, NUMA topology may be hidden by the cgroup; rely on environment knobs, not detection.

## Lab

The `LocalityDemo` allocates large arrays from many threads and then reads them from *different* threads. On a NUMA box the second phase shows variance; pin with `numactl` to see it disappear.
