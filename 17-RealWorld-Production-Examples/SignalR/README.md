# SignalR / realtime servers

A SignalR server can hold tens of thousands of WebSocket connections per instance. The concurrency model is *one logical client* (a `Hub` instance is per-call, but the connection is long-lived) with broadcast / per-group fan-out.

## Concurrency hot-points

1. **Connection state.** A dictionary of connection-id → user metadata. `ConcurrentDictionary` covers it.
2. **Broadcasting.** Sending the same message to many clients: parallel, bounded.
3. **Backpressure**. Slow clients shouldn't slow the server's send loop. Each connection has its own `Pipe<T>`-style buffer.
4. **Group membership**. Adding/removing connections from groups happens concurrently; SignalR's `IGroupManager` is thread-safe.

## Hub method patterns

A hub method runs per call:

```csharp
public sealed class ChatHub : Hub
{
    public async Task SendMessage(string user, string message, CancellationToken ct)
    {
        await Clients.All.SendAsync("ReceiveMessage", user, message, ct);
    }
}
```

`Clients.All.SendAsync` fans out to every connected client. SignalR uses bounded per-connection write queues internally; backpressure is built in.

## Broadcasting from a `BackgroundService`

```csharp
public class TickerService(IHubContext<ChatHub> hub) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await hub.Clients.All.SendAsync("Tick", DateTimeOffset.UtcNow, ct);
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }
}
```

`IHubContext<T>` is the way to send to clients from outside a hub method. Inject it into any service.

## Slow clients

If one client's send queue is full, SignalR drops the connection (with `MaximumParallelInvocationsPerClient` and per-connection limits in the options). Configurable via `HubOptions`:

```csharp
services.AddSignalR(options =>
{
    options.ClientTimeoutInterval   = TimeSpan.FromSeconds(60);
    options.HandshakeTimeout        = TimeSpan.FromSeconds(15);
    options.MaximumParallelInvocationsPerClient = 1;
    options.MaximumReceiveMessageSize = 64 * 1024;
});
```

## Sharding across instances

For horizontal scale, SignalR uses a backplane (Redis, Azure SignalR Service). Messages broadcast on instance A reach clients on instance B via the backplane. Concurrency-wise:

- Each instance handles its own clients.
- Backplane fan-out is async and bounded.
- Broadcast scales with `O(clients)`; group sends with `O(group size)`.

## Concurrency anti-patterns

1. **`Hub.Context.User` in a `Task.Run` body** — `Hub.Context` is per-invocation; capturing it for delayed work is undefined.
2. **Singleton state mutated from hub methods without sync** — use locks or `ConcurrentDictionary`. Many hub instances service one connection's calls in parallel.
3. **Long-running work in a hub method** — blocks the connection's send/receive loop. Move to a `BackgroundService` and report progress via `Clients.Caller.SendAsync(...)`.

## Observability

- `signalr.dotnet.connections.active`
- `signalr.dotnet.messages.sent`
- `signalr.dotnet.errors`

Connection lifecycle events log via `ILogger`. For per-message tracing, add OpenTelemetry SignalR instrumentation.

## Bounded throughput

A typical .NET 10 instance handles ~50k concurrent WebSocket connections with low CPU. The bottleneck is usually downstream services or the backplane, not the SignalR server itself.
