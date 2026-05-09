# Native AOT and concurrency

Native AOT compiles your .NET app to a single native binary. No JIT, no IL at runtime, no managed metadata for reflection. Boots in ms, deploys as ~10-30 MB.

## Concurrency-relevant differences from JIT-compiled .NET

| Aspect | JIT | Native AOT |
|---|---|---|
| `async`/`await` | yes (full support) | yes (full support) |
| `Task` / `Channel<T>` | yes | yes |
| ThreadPool | yes (full) | yes (full) |
| Locks, atomics, primitives | identical | identical |
| Tier-1 dynamic PGO | yes (default) | n/a (one-time AOT) |
| Reflection over async builders | yes | restricted |
| `[AsyncMethodBuilder]` custom | yes | yes (with attributes) |
| Global trimming concerns | n/a | yes — unused code removed |
| Source generators required for some APIs | optional | strongly preferred |

## What you keep

The whole concurrency stack works in AOT:

- `Task` / `ValueTask` / `async` / `await`
- `Channel<T>` / `BlockingCollection<T>` / `ConcurrentDictionary<K,V>` / `FrozenDictionary<K,V>`
- `lock`, `Monitor`, `Mutex`, `SemaphoreSlim`, `ManualResetEventSlim`, etc.
- `Parallel.For`, `Parallel.ForEachAsync`, PLINQ
- `System.Threading.RateLimiting`
- TPL Dataflow

## What you lose

- **Dynamic code paths.** No `Reflection.Emit`, no expression-tree compilation. Some libraries that lean on these (older ORMs, mocking frameworks) don't work; modern ones use source generators.
- **`System.Threading.Tasks.Dataflow`** — works but the linker is conservative; some metadata may be trimmed too aggressively. Test thoroughly.
- **Runtime DI containers** that compile factories at runtime. The .NET 8+ DI container's compiled lambdas degrade gracefully to reflection in AOT, with a perf cost.

## Performance characteristics

- **Faster startup** (10–100×). Great for serverless / CLI tools.
- **Lower memory** (no JIT engine).
- **Slightly slower steady-state** than JIT with dynamic PGO. The JIT can specialise for runtime-observed types; AOT can't.
- **No tiered compilation** — what AOT compiles is what you run.

The steady-state gap between AOT and JIT-with-PGO has narrowed each release; .NET 10 is closer than ever.

## Recommendations

- **Always-on services** (web servers, daemons): JIT is usually marginally faster and easier to deploy.
- **Lambdas / serverless**: AOT for sub-100ms cold starts.
- **CLI tools**: AOT for portability and speed.
- **High-perf concurrency**: either works; measure.

## Setup

```xml
<PropertyGroup>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <StripSymbols>true</StripSymbols>
</PropertyGroup>
```

```bash
dotnet publish -r linux-x64 -c Release
# output: bin/Release/net10.0/linux-x64/publish/MyApp (single binary)
```

## Concurrency caveats specific to AOT

1. **`async` over interfaces** can hit AOT trim warnings if the interface has many implementations and the linker can't see them all. Add `[DynamicDependency]` or rooting attributes.
2. **`AsyncLocal<T>`** is fine.
3. **Reflection over `Task<T>`** (e.g., to detect "is this Task<int>?") may need rooting.
4. **Source-generated regex / JSON / logging** is preferred over runtime equivalents in AOT.
5. **`P/Invoke`** uses `[LibraryImport]`'s source generator — fast in both modes; AOT-friendly by default.

## Verifying

```bash
dotnet publish -r linux-x64 -c Release /p:PublishAot=true
# warnings about trimming/AOT issues appear here. Treat them as errors before shipping.
```
