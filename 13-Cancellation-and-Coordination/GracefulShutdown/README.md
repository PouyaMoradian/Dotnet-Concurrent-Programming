# Graceful shutdown

A graceful shutdown:

1. **Stops accepting new work.**
2. **Drains in-flight work** with a deadline.
3. **Cleans up resources** (flushes buffers, closes connections, releases locks).
4. **Exits with code 0** if successful, non-zero if forced.

In .NET hosts (`Microsoft.Extensions.Hosting`), this is wired via `IHostApplicationLifetime` + `BackgroundService` + `IHostedService.StopAsync(ct)`.

## The host's contract

`StopAsync(CancellationToken ct)` is called when the host is shutting down. The token's deadline (default 5 seconds, configurable via `HostOptions.ShutdownTimeout`) is the *graceful* window. After that, the process is terminated regardless.

## Pattern: `BackgroundService` with proper shutdown

```csharp
public class WorkerService(IServiceScopeFactory scope) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 1. ExecuteAsync receives a token that's signalled at StopAsync.
        await foreach (var item in IngestAsync(stoppingToken))
        {
            // 2. Each unit of work also takes the token.
            using var s = scope.CreateScope();
            await s.ServiceProvider.GetRequiredService<IHandler>().HandleAsync(item, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        // 3. By default, the base StopAsync triggers stoppingToken and awaits ExecuteAsync.
        // Override only to add custom drain logic before/after.
        await base.StopAsync(ct);
    }
}
```

## Pattern: drain a `Channel<T>` on shutdown

```csharp
public sealed class Pipeline : IHostedService
{
    private readonly Channel<Item> _ch = Channel.CreateBounded<Item>(1024);
    private Task? _runner;
    private CancellationTokenSource? _runnerCts;

    public Task StartAsync(CancellationToken ct)
    {
        _runnerCts = new CancellationTokenSource();
        _runner = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in _ch.Reader.ReadAllAsync(_runnerCts.Token))
                    await ProcessAsync(item);
            }
            catch (OperationCanceledException) { /* expected */ }
        });
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _ch.Writer.Complete();                   // 1) stop accepting
        try { await (_runner ?? Task.CompletedTask).WaitAsync(ct); }   // 2) drain with deadline
        catch (TimeoutException) { _runnerCts?.Cancel(); }              // 3) escalate to cancel
    }
}
```

## Console apps

Subscribe to `Console.CancelKeyPress` (Ctrl+C) and signal a CTS:

```csharp
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;        // don't let the runtime kill us — let us shut down
    cts.Cancel();
};
await RunAsync(cts.Token);
```

In .NET 6+, `IHost.RunAsync()` does this automatically.

## Linux signals

`SIGTERM` triggers the same shutdown path on Linux. `SIGINT` (Ctrl+C) does too. The `IHostApplicationLifetime` infrastructure handles both.

## Don't do these

- **`Environment.Exit`** in worker code: skips Dispose paths, loses in-flight data.
- **Wait synchronously on the main thread for everything**: `Console.ReadLine()` doesn't surface signals. Use `host.WaitForShutdownAsync()`.
- **Catch `OperationCanceledException` and continue.** During shutdown, that exception means "we're going". Re-throw or break out of the loop.

## Verifying you shut down gracefully

A small ritual at exit:

```csharp
await host.RunAsync();    // returns after StopAsync completes
Console.WriteLine("clean shutdown");
return 0;
```

If you see "clean shutdown" in your logs, you did. If you see SIGKILL in dmesg / a non-zero exit code from the host, you didn't — investigate the timeout and which task didn't honour cancellation.
