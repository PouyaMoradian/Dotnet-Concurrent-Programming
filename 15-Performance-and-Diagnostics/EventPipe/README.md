# EventPipe

EventPipe is the cross-platform transport behind every `dotnet-*` diagnostic CLI. It carries events from inside the runtime/EventSources to a Unix-domain socket / named pipe; subscribers read from that socket.

## Why it matters

ETW is Windows-only. EventPipe gives you the same shape — events, providers, keywords, levels — on Linux/macOS. `dotnet-trace`, `dotnet-counters`, and `dotnet-monitor` are all EventPipe consumers.

## Provider shape

An EventSource has:

- A **name** (`Microsoft-Windows-DotNETRuntime`, your custom one).
- A **GUID** (auto-derived from the name).
- A set of **events** with IDs, levels (Verbose/Informational/Warning/Error/Critical), and keywords (bitmask).

When subscribing:

```bash
dotnet-trace collect --providers MyApp:0xFFFFFFFFFFFFFFFF:5,Microsoft-Windows-DotNETRuntime:0x10000:4
```

`MyApp` = name. `0xFFFF...` = "all keywords" mask. `5` = "Verbose" level (0=LogAlways, 1=Critical, 2=Error, 3=Warning, 4=Informational, 5=Verbose).

## Writing an EventSource

```csharp
[EventSource(Name = "MyApp")]
public sealed class MyEventSource : EventSource
{
    public static readonly MyEventSource Log = new();

    [Event(1, Level = EventLevel.Informational, Message = "request {0}")]
    public void Request(string path) => WriteEvent(1, path);

    [Event(2, Level = EventLevel.Warning, Message = "slow path {0}: {1} ms")]
    public void SlowPath(string path, long ms) => WriteEvent(2, path, ms);
}
```

Use:

```csharp
MyEventSource.Log.Request("/api/foo");
```

When no one's listening, the cost is one `if (!IsEnabled) return;` — *near-zero*. When someone's listening, each event costs ~hundreds of ns. Safe to leave in production.

## Keywords

Keywords let consumers filter:

```csharp
public sealed class MyEventSource : EventSource
{
    public static class Keywords
    {
        public const EventKeywords Networking = (EventKeywords)1;
        public const EventKeywords Database = (EventKeywords)2;
        public const EventKeywords Logging = (EventKeywords)4;
    }

    [Event(1, Keywords = Keywords.Networking, Level = EventLevel.Informational)]
    public void HttpRequest(string url) => WriteEvent(1, url);
}
```

Subscribing:

```bash
dotnet-trace collect --providers MyApp:0x1:5    # only Networking
```

## Sampling vs all

EventPipe can also collect *sampled* CPU stacks. That's what `dotnet-trace`'s default does — it includes a built-in "Sample" provider sampling at ~1 kHz. The result is a flame-graph-friendly trace.

## In-process listening

Sometimes you want to subscribe within the same process (e.g., a "/diagnostics" admin endpoint). Use `EventListener`:

```csharp
class Listener : EventListener
{
    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "MyApp") EnableEvents(source, EventLevel.Verbose);
    }
    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        Console.WriteLine($"{e.EventName}: {string.Join(", ", e.Payload ?? Array.Empty<object?>())}");
    }
}
```

Used by libraries that want to expose internal events without external tooling.

## Performance considerations

- `WriteEvent(int, …)` is generic-overloaded for most parameter shapes. The marshalling is fast.
- Avoid string allocations in the hot path: log primitives where possible; format only on subscriber side.
- `IsEnabled()` check is cheap; gate non-trivial argument prep on it.
