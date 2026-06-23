# Buffer pools on NUMA

`ArrayPool<T>` is the canonical .NET answer to "stop allocating and freeing the same-sized buffer in a hot loop". On a single-node machine it's almost always right. On a NUMA box, the most-common form — `ArrayPool<T>.Shared` — is a hazard. This file is the why and the alternative.

## What `ArrayPool<T>.Shared` does

There's exactly one shared pool per process. It internally maintains per-CPU buckets and a global fallback. When you `Rent`:

1. Look at the current CPU's local bucket for a buffer of the right size class.
2. If found, return it.
3. If not, fall back to a shared global bucket.
4. If still not found, allocate a fresh one.

When you `Return`:

1. Put it back in the current CPU's bucket.

The "current CPU" is whoever is calling. The buffer doesn't carry a tag identifying its NUMA origin.

## The NUMA hazard

Imagine an 8-core box with 2 nodes (4 cores per node). A request thread on node 0 rents a 64 KB buffer; the buffer was first-touched somewhere ago — say node 0. The thread fills it (writes still local), responds, and returns the buffer.

Now a later request comes in, scheduled on node 1. It rents from the shared pool — and gets that buffer. The buffer's physical pages still live on node 0. Every byte the node 1 thread reads or writes is a remote access. The cost: ~50–100 ns extra latency per cache line, possibly bandwidth saturation on the interconnect.

The longer the process lives, the more buffers spread across nodes, and the worse the asymmetry becomes. In a long-running server you eventually pool every buffer everywhere; reuse degenerates to "any buffer might be remote".

## When you can ignore this

The hazard depends on having actual NUMA hardware. On a single-node host (most laptops, many cloud VMs) `ArrayPool<T>.Shared` is fine.

If your buffers are short-lived enough that they stay in L1/L2 anyway, the hazard is also minor — the cache hides DRAM-level NUMA effects.

The hazard *matters* when:

- Multi-node hardware with NUMA exposed to the guest.
- Buffers are large enough to overflow cache (≥ ~256 KB).
- Re-reads or re-writes are sufficient to make DRAM-level traffic dominate.

## The alternatives

### 1. Per-node pools

```csharp
public sealed class NumaArrayPool<T>
{
    private readonly ArrayPool<T>[] _pools;

    public NumaArrayPool(int nodeCount)
    {
        _pools = new ArrayPool<T>[nodeCount];
        for (int i = 0; i < nodeCount; i++)
            _pools[i] = ArrayPool<T>.Create();   // private, non-shared
    }

    public T[] Rent(int minSize, int nodeIndex) => _pools[nodeIndex].Rent(minSize);
    public void Return(T[] array, int nodeIndex) => _pools[nodeIndex].Return(array);
}
```

You need to know which node the thread is on. The easiest: stash a `[ThreadStatic]` `nodeIndex` set once at pool startup (with the thread pinned to that node).

The expectation: a buffer rented and returned by threads on node *i* lives on node *i*, by induction from first-touch.

### 2. `ArrayPool<T>.Create()` (not Shared)

Create your own pool, scoped to one node or one pipeline. The pool itself is not shared with other parts of the app; you control which threads use it.

```csharp
private readonly ArrayPool<byte> _pipelineAPool = ArrayPool<byte>.Create();
private readonly ArrayPool<byte> _pipelineBPool = ArrayPool<byte>.Create();
```

### 3. Pre-allocate and reuse explicitly

For very hot pipelines, skip the pool entirely. Allocate a slab on the consumer's thread once, reuse it for life. The classic ring-buffer-of-fixed-size-buffers pattern.

```csharp
// On consumer thread:
private static readonly byte[][] _slots = new byte[16][];
static MyConsumer()
{
    for (int i = 0; i < _slots.Length; i++) _slots[i] = GC.AllocateArray<byte>(64 * 1024, pinned: true);
    // first-touch happens here, on the consumer's thread/node.
}
```

`GC.AllocateArray<T>(size, pinned: true)` allocates on the **Pinned Object Heap**; the array never moves. Good for long-lived NUMA-pinned buffers.

### 4. Don't pool large buffers

If the buffer is large enough to matter for NUMA, it's probably large enough that the GC's **LOH** (Large Object Heap) or **POH** (Pinned Object Heap) suffices. Reuse with explicit slabs (option 3), and let small buffers go through `ArrayPool<T>.Shared` where the cost is negligible.

## A diagnostic

If you suspect this is biting you:

1. Run on `numactl --membind=0 --cpunodebind=0` (single node). Compare RPS / latency to unconstrained.
2. If the bound run is much faster, NUMA is in play.
3. Inspect `perf c2c` to see cross-node line transfers.

`perf c2c` (Linux ≥4.10) reports "the line at virtual address X was modified on node N0 and read on node N1 K times" — pinpoints the bad lines.

## Practical takeaways

- `ArrayPool<T>.Shared` is great on single-node hardware.
- On multi-node hardware with large hot buffers, prefer per-node or per-pipeline pools.
- Pre-allocated slabs on POH are the most predictable for latency-critical paths.
- Always check whether the pool is even the bottleneck before optimising it.

## Lab

There's no chapter 0 demo for buffer-pool NUMA specifically; chapter 15 has memory-allocation benchmarks. To make it concrete in a small reproducer:

1. Pin one producer thread to node 0, one consumer to node 1.
2. Allocate buffers with `ArrayPool<byte>.Shared.Rent` *on the consumer*; fill on the producer.
3. Time end-to-end. Then redo with `ArrayPool<byte>.Create()` on each side. The difference is the NUMA penalty.

## Further reading

- **`ArrayPool<T>` source** in the .NET runtime — read what's in `TlsOverPerCoreLockedStacksArrayPool<T>`.
- **Stephen Toub — *Performance improvements in .NET 8/9*** — sections on `ArrayPool` improvements.
- **`perf c2c` documentation** — the diagnostic tool for cross-node line transfers.
