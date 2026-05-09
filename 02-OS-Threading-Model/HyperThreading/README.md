# Hyper-Threading / SMT

A physical core has one set of execution units (ALUs, FPU, load/store units). With **SMT** (Intel: Hyper-Threading; AMD: SMT) the core duplicates the *architectural state* — registers, flags, instruction pointer — so two **logical** processors can be in flight at once on the same execution units.

The point: when one logical processor stalls (cache miss, branch mispredict, dependency), the other can use the otherwise-idle units. This raises **utilisation**, not raw clock speed.

## The math

| Workload | SMT speedup |
|---|---|
| Memory-bound (lots of stalls) | ~1.3–1.5× |
| ALU-bound, dense, vectorised | ~1.05–1.1× (sometimes negative because the threads contend for L1) |
| Mixed | ~1.2–1.3× typical |

So `Environment.ProcessorCount = 16` on an 8-core/16-thread CPU does *not* mean 16× CPU-bound parallelism. The realistic ceiling for compute-bound code is closer to ~10–11×.

## Side channels and security

SMT shares L1 caches and branch predictor state between siblings. Several **microarchitectural side-channel attacks** (L1TF, MDS, ZombieLoad) leveraged this. Mitigations include:

- **Disable SMT entirely** (BIOS option). Some cloud providers do this for security-critical fleets.
- **Co-scheduling** (Linux `core scheduling` cgroups) — only sibling threads from the *same trust domain* are co-scheduled.
- **Spectre-BHB / Retbleed mitigations** — the kernel inserts barriers; minor perf cost.

For .NET-on-shared-host scenarios (cloud, multi-tenant Kubernetes), assume SMT is enabled and that you do not control siblings. Don't make security assumptions based on isolation that the silicon can't provide.

## Practical advice

- **Server work (request handling, microservices):** leave SMT on. The IO/cache-miss overlap is exactly the workload SMT helps.
- **Heavy parallel compute (ML, encoding, simulation):** experiment. Sometimes *disabling* SMT gives more deterministic per-thread throughput; sometimes leaving it on wins via overlap.
- **Don't pin to siblings.** If you affinitise threads, pick *different* physical cores. Linux `lscpu -e` shows the topology.

## Detecting SMT topology in .NET

```csharp
// Linux: parse /sys/devices/system/cpu/cpu*/topology/thread_siblings_list
// Windows: GetLogicalProcessorInformationEx (P/Invoke)
```

There is no portable BCL API for this. If you need topology-aware code, wrap a P/Invoke and ship per-OS implementations.
