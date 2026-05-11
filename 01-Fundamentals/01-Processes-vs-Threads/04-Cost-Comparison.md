# Cost comparison — threads, tasks, processes, async

Every chapter in this repo assumes you have an intuition for the *relative* costs of the concurrency primitives. This page makes those intuitions quantitative.

## Cheat sheet (order of magnitude)

| Operation | Wall time (single op) | What's happening |
|---|---|---|
| Method call (managed) | ~1 ns | A few register moves |
| Virtual call | ~1–2 ns | One vtable load |
| `Task.CompletedTask` await (sync path) | ~5 ns | The state machine elided |
| `ValueTask` of synchronous result | ~5–10 ns | Allocation-free |
| Cache-resident memory read | ~1 ns (L1) – ~10 ns (L3) | — |
| Main memory read (cache miss) | ~100 ns | — |
| `lock` (uncontended) | ~20–50 ns | One atomic CAS |
| `Interlocked.Increment` (contended) | ~30–300 ns | Depends on contention |
| Allocating a 64-byte object on the GC heap | ~10–30 ns | Gen 0 bump pointer |
| `Task.Run(() => …)` (warm pool) | ~1–3 µs | Queue + worker pickup |
| Context switch (same process) | ~1–3 µs | Save/restore registers |
| Context switch (different process) | ~3–10 µs | Plus TLB flush |
| `new Thread().Start()` + `Join()` | ~50–500 µs | OS thread creation |
| `Process.Start` (managed re-exec) | ~5–50 ms | Whole `clone`/`CreateProcess` |
| `Thread.Sleep(0)` | ~0.5–5 µs | Yield to scheduler |
| Disk seek (HDD) | ~5–10 ms | Mechanical |
| SSD read (random 4 KB) | ~50–150 µs | NVMe |
| Localhost TCP round-trip | ~30–100 µs | Kernel + loopback |
| Same-region cross-host TCP round-trip | ~0.5–2 ms | Network |

A few patterns to read out of this:

- **The thread pool's startup cost is three orders of magnitude smaller than a fresh OS thread's.** That's the entire reason it exists.
- **An async no-op is another three orders of magnitude smaller than a pooled task.** That's the entire reason `ValueTask` exists — for hot paths where most calls *don't* need to suspend.
- **Locks are cheaper than people think *when uncontended*.** A `lock` on an object nobody else is touching is a single atomic CAS. The cost shows up only under contention.
- **A context switch is fast in nanoseconds but slow in cache misses.** That's why work-stealing schedulers care about locality.

## The actual demo on this machine

Run `ProcessVsThreadDemo` and you'll see real numbers for your hardware. The output looks like:

```
  1000 dedicated Threads:  start+join =   213 ms
  1000 Tasks (Task.Run):   start+wait =     8 ms
  1000 async no-ops:       start+wait =     1 ms
```

Read the three rows together: the first row builds 1000 OS threads, each with a 1 MB stack reservation, and tears them down. The second row queues 1000 work items into a pool that has perhaps 16 threads, which pick them up in turn. The third row creates 1000 already-completed `Task` objects that never even hit the queue.

## When the gap matters

The 100× difference between `new Thread` and `Task.Run` only matters when you're starting work items often. Two scenarios where people *should* care:

- **Per-request work in a server.** 10,000 RPS × `new Thread` per request = ~1 ms of pure thread overhead per request, with a steadily growing TLB pressure. Same on `Task.Run` ≈ 10 µs. Same on async ≈ < 1 µs. The async path is the only one that scales.
- **Fork/join parallel decomposition.** A `Parallel.For` with chunks of work that take 10 ms each is fine. A `Parallel.For` with chunks that take 100 ns each will spend more time in pool overhead than in real work — solve a bigger chunk per iteration.

And one scenario where people *over*care:

- **A single one-shot background worker.** If your app spins up *one* worker for the lifetime of the process, `new Thread(work) { IsBackground = true }.Start()` is fine. It costs ~100 µs once, and you get a dedicated thread whose hill-climbing pressure doesn't perturb the pool. This is the case for an audio mixer, a background log flusher, a memory-pressure watcher.

## Why the cost of `new Thread` isn't easily reducible

You might wonder why `new Thread` is so much more expensive than `Task.Run`. The costs come from:

1. **OS resources allocated.** Kernel TCB, thread ID, stack reservation, descriptor in `/proc/<pid>/task/` (Linux).
2. **First-run startup.** Stack guard pages get committed lazily on first touch, but the first few KB get touched immediately.
3. **GC bookkeeping.** The CLR registers the thread with the GC, allocates the managed `Thread` object, and threads it into the runtime's thread list.
4. **Synchronisation.** Inserting into the runtime's thread list is itself locked.

None of those are reducible without changing what a "thread" means at the OS level. Hence the pool — amortise these costs across many work items.

## The benchmarking caveat

All numbers above are *order-of-magnitude*. If you need precise numbers for a decision, use BenchmarkDotNet (see [15-Performance-and-Diagnostics](../../15-Performance-and-Diagnostics/) and the `BENCHMARKS/` folder at the repo root). The demo in this chapter measures with `Stopwatch` and is only good to ~10% — fine for "200 ms vs 8 ms", not fine for "is option A 5% better than option B".
