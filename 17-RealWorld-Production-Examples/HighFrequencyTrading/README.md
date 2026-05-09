# High-frequency trading — concurrency at microsecond scale

HFT systems aim for sub-microsecond decision latency. The "thread pool" + `async/await` model is fine for everything *except* the hot inner loops, which use:

- **Pinned threads** (one per core, isolated from the OS scheduler).
- **Lock-free SPSC ring buffers** between stages.
- **Zero-allocation hot paths** (Span, stackalloc, ArrayPool, pre-allocated structs).
- **CPU isolation** (Linux `isolcpus`, NUMA-pinned).
- **Hardware timestamps** read via `rdtsc` / `cntvct_el0`.

## A typical decomposition

```
Receiver   → SPSC →  Decoder   → SPSC →  Strategy   → SPSC →  Sender
(pinned)             (pinned)             (pinned)             (pinned)
core 2               core 3                core 4              core 5
```

Each stage owns one pinned thread. SPSC rings between them. **No locks anywhere on the hot path.**

## Pinning a thread to a core

```csharp
[LibraryImport("libc", SetLastError = true)]
private static partial int sched_setaffinity(int pid, IntPtr cpusetsize, ref ulong mask);

static void PinTo(int cpu)
{
    ulong mask = 1UL << cpu;
    sched_setaffinity(0, (IntPtr)sizeof(ulong), ref mask);
}

var t = new Thread(() => { PinTo(2); RunReceiver(); }) { IsBackground = false, Priority = ThreadPriority.AboveNormal };
t.Start();
```

In production you'd also `mlock` the process memory and disable C-states (`/sys/devices/system/cpu/cpu*/cpuidle/state*/disable`).

## Allocation-free SPSC ring (sketch)

```csharp
public sealed class SpscRing<T> where T : struct
{
    private readonly T[] _buf;
    private readonly int _mask;
    private long _head;          // producer-only writer; consumer reads
    private long _tail;          // consumer-only writer; producer reads

    public SpscRing(int sizePow2) { _buf = new T[sizePow2]; _mask = sizePow2 - 1; }

    public bool TryPush(in T v)
    {
        var head = _head;
        if (head - Volatile.Read(ref _tail) >= _buf.Length) return false;
        _buf[head & _mask] = v;
        Volatile.Write(ref _head, head + 1);     // release
        return true;
    }

    public bool TryPop(out T v)
    {
        var tail = _tail;
        if (tail >= Volatile.Read(ref _head)) { v = default; return false; }
        v = _buf[tail & _mask];
        Volatile.Write(ref _tail, tail + 1);
        return true;
    }
}
```

Two volatile pointers; one writer per pointer. No CAS. Allocation-free if `T` is a value type and the buffer is reused.

## Cache awareness

Pad shared writer counters to separate cache lines (see [00/False-Sharing](../../00-Prerequisites/False-Sharing/)). On a hot ring, false sharing across `_head`/`_tail` will halve your throughput.

```csharp
[StructLayout(LayoutKind.Explicit, Size = 256)]
public struct PaddedHeadTail
{
    [FieldOffset(0)]   public long Head;
    [FieldOffset(128)] public long Tail;
}
```

## What to *not* do

- **`Task.Run`** — pool threads, no affinity, no priority.
- **`Channel<T>`** — fast, but allocates per item; HFT wants stack-allocated.
- **Async/await** — too much overhead for sub-microsecond paths. Sync calls + busy-wait is the model.
- **`lock`** — kernel waits are too slow.
- **`new` in the hot loop.** Anything.

## What to do for the *non-hot* paths

The control plane (order entry from REST, monitoring, config reload) uses normal ASP.NET Core patterns. The hot path is the exception, not the rule.

## Realistic perf

With careful C# in .NET 10, sub-microsecond decision latency from packet ingress to outgoing order is achievable for simple strategies. Sub-100-ns decisions usually require C++ or Rust co-running.
