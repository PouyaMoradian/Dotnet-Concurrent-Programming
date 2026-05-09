# dotnet-counters

The simplest tool: live counter monitoring. Always your first move when something's wrong.

```bash
dotnet tool install -g dotnet-counters
dotnet-counters monitor --process-id $(pidof MyApp)
```

You get a live-updating screen with a default set of counters (System.Runtime, Microsoft.AspNetCore.Hosting if applicable, etc.).

## Counters that matter for concurrency

| Counter | Provider | Meaning |
|---|---|---|
| `cpu-usage` | System.Runtime | Process CPU% |
| `working-set` | System.Runtime | Resident memory |
| `gc-heap-size` | System.Runtime | Total managed heap |
| `gen-0/1/2-gc-count` | System.Runtime | GC frequency |
| `monitor-lock-contention-count` | System.Runtime | Lock contention rate |
| `threadpool-thread-count` | System.Runtime | Live thread pool workers |
| `threadpool-queue-length` | System.Runtime | Pending work items |
| `threadpool-completed-items-count` | System.Runtime | Throughput |
| `time-in-gc` | System.Runtime | % time in GC |
| `alloc-rate` | System.Runtime | Bytes allocated per second |

## Targeted monitoring

```bash
# only the ThreadPool counters
dotnet-counters monitor --process-id <pid> System.Runtime --counters threadpool-thread-count,threadpool-queue-length,threadpool-completed-items-count

# update interval (default 1 second)
dotnet-counters monitor --process-id <pid> --refresh-interval 5
```

## Collect to a file

```bash
dotnet-counters collect --process-id <pid> --duration 00:01:00 --output counters.json
```

Useful for post-hoc analysis or sending to a teammate. Output formats: csv, json.

## Custom counters from your code

`System.Diagnostics.Metrics` (since .NET 6) is the modern API:

```csharp
using System.Diagnostics.Metrics;

var meter = new Meter("MyApp");
var requests = meter.CreateCounter<long>("requests-total");
var inflight = meter.CreateUpDownCounter<long>("requests-in-flight");

requests.Add(1, new KeyValuePair<string, object?>("path", "/api/foo"));
```

Subscribe with `dotnet-counters monitor --counters MyApp`. They show up live.

For long-running services, integrate with OpenTelemetry → Prometheus / Honeycomb / Datadog. The same `Meter`s are emitted to OTel.

## A first-look workflow

```bash
dotnet-counters monitor --process-id $(pidof MyApp)
```

Look for:

- **High `cpu-usage` + non-zero `time-in-gc`** → maybe GC pressure; investigate with `dotnet-trace`.
- **High `monitor-lock-contention-count`** → contention; investigate which lock with PerfView.
- **`threadpool-queue-length` rising** → starvation; investigate sync-over-async patterns.
- **`alloc-rate` very high (> 100 MB/s)** → allocator-heavy code; consider pools / Span.
- **`gc-fragmentation-ratio` high** → pinning issues or LOH; investigate.

## Limitations

- Counters update at 1-second granularity; not for sub-second resolution.
- Some counters are derived (rate counters); they need 2+ samples to be valid.
- Production-safe — overhead is < 0.1%.
