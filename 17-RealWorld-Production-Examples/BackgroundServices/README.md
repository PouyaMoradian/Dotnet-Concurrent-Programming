# BackgroundServices

`Microsoft.Extensions.Hosting.BackgroundService` is the right shape for any long-running task in a .NET host. It's IDE-discoverable, DI-friendly, and integrated with the host lifecycle.

## The skeleton

```csharp
public sealed class MyWorker(IDependency dep, ILogger<MyWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("worker started");
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await DoOneIterationAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // clean shutdown
        }
        catch (Exception ex)
        {
            log.LogCritical(ex, "worker died unexpectedly");
            throw;
        }
        finally
        {
            log.LogInformation("worker stopped");
        }
    }
}
```

Register: `services.AddHostedService<MyWorker>();`

## Important defaults

- **`stoppingToken`** is the same token across the worker's lifetime; signaled when `IHost.StopAsync` is called.
- **An exception from `ExecuteAsync`** kills the *worker* but, by default, **also kills the host** (since .NET 6's behaviour change). Configure `HostOptions.BackgroundServiceExceptionBehavior` to `Ignore` if that's not what you want — but think twice before silencing crashes.
- **Shutdown timeout** defaults to 5 seconds. Configure `HostOptions.ShutdownTimeout` if you need longer.

## Common patterns

### Periodic timer

```csharp
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
while (await timer.WaitForNextTickAsync(stoppingToken))
{
    await DoTickAsync(stoppingToken);
}
```

`PeriodicTimer` (.NET 6+) is the preferred way over `Timer` for periodic work — no callback ceremony, async-native, `DisposeAsync`-friendly.

### Channel-fed worker

```csharp
private readonly Channel<WorkItem> _ch;

protected override async Task ExecuteAsync(CancellationToken ct)
{
    await foreach (var item in _ch.Reader.ReadAllAsync(ct))
    {
        await ProcessAsync(item, ct);
    }
}
```

External code submits via `_ch.Writer.WriteAsync(item)`. Bounded for backpressure.

### Multiple parallel workers

Don't spawn N `BackgroundService`s if they're identical. Spawn one and use `Parallel.ForEachAsync`:

```csharp
protected override Task ExecuteAsync(CancellationToken ct) =>
    Parallel.ForEachAsync(
        SourceAsync(ct),
        new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
        async (item, innerCt) => await ProcessAsync(item, innerCt));
```

## Anti-patterns

1. **`async void`** in any helper — exception bubbles up to the process. Always `async Task`.
2. **Catching and swallowing `OperationCanceledException`** indiscriminately — you lose the signal that you're shutting down.
3. **Using `_stoppingToken.Register(...)`** for "do something on shutdown" without ensuring the registration is disposed — leaks.
4. **Long synchronous loops without checking `stoppingToken`** — the host can't shut you down; SIGKILL after the timeout.
5. **Creating DI scopes incorrectly** — `IServiceProvider` injected at root scope; always `using var scope = _sp.CreateScope()` before resolving scoped services.

## Health checks

A worker that's been running for a while without making progress is a problem. Expose health:

```csharp
private DateTimeOffset _lastTick;

public class WorkerHealth(MyWorker w) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(...) =>
        Task.FromResult((DateTimeOffset.UtcNow - w._lastTick) < TimeSpan.FromMinutes(2)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy());
}
```

Wire to `app.MapHealthChecks("/health")`.

## Not all hosted services should derive from BackgroundService

If your service starts something but doesn't loop (`StartAsync` opens connections, `StopAsync` closes them), implement `IHostedService` directly — `BackgroundService` is for the looping case.
