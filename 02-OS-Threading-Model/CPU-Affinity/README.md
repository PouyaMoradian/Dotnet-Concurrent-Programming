# CPU affinity

**Affinity** = the set of CPUs a thread is allowed to run on. By default, threads can run on any CPU in the process's affinity mask, and the scheduler decides.

## Why pin?

1. **Cache locality.** A thread that always runs on CPU 0 keeps L1/L2 hot. Migrations cost ~5–20 µs of cold-cache penalty.
2. **NUMA.** Pinning a thread to a node means its first-touch allocations stay local.
3. **Real-time-ish guarantees.** Reserving a core for a worker means no other process steals time.
4. **Reproducibility for benchmarking.** Eliminates migration variance.

## Why **not** pin?

1. **The scheduler is smarter than you most of the time** about load balancing.
2. **Pinned threads cannot move when their core is busy** — they sit in a runnable queue while another core is idle.
3. **Containers may rewrite affinity** (cgroup `cpuset`); your settings can be silently wrong.

## Process-level affinity in .NET

```csharp
using var p = Process.GetCurrentProcess();
p.ProcessorAffinity = (IntPtr)0b00001111; // logical CPUs 0..3 only
```

This affects the **default** affinity for new threads. Existing threads keep theirs.

## Thread-level affinity (P/Invoke required)

```csharp
// Linux
[LibraryImport("libc", SetLastError = true)]
private static partial int sched_setaffinity(int pid, IntPtr cpuSetSize, ref ulong mask);

// Windows
[LibraryImport("kernel32.dll", SetLastError = true)]
private static partial UIntPtr SetThreadAffinityMask(IntPtr hThread, UIntPtr dwThreadAffinityMask);

// Windows: GetCurrentThread() returns a pseudo-handle; valid for affinity calls.
```

## When pinning helps measurably

- **HFT / low-latency networking.** Reserve 2–4 cores; pin the receive thread, the matcher, the sender. Use `mlock` and isolated cgroups. Disable C-states.
- **Game engines / audio.** A real-time audio thread that must hit 1 ms cycles.
- **Benchmarking.** `taskset -c 0 dotnet run -c Release` removes scheduler variance.

## Production pattern: warm-up and pin

```csharp
public sealed class HotPathRunner
{
    public void Start()
    {
        var t = new Thread(Run) { IsBackground = false, Priority = ThreadPriority.AboveNormal };
        t.Start();
    }

    private void Run()
    {
        PinCurrentThreadTo(cpu: 3);          // platform-specific helper
        WarmUp();                             // touch your buffers, JIT-trigger your code paths
        while (true) DoIteration();
    }
}
```

Don't do this on general server code — leave the scheduler alone.

## The .NET ThreadPool ignores you

Setting `ProcessorAffinity` on a process does propagate to pool threads. But `Task.Run`/the pool itself does not honour any per-task affinity hint. If you need a pinned worker for hot work, **don't use the pool**; use `new Thread(...)` plus pinning.
