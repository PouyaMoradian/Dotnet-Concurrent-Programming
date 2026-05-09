using System.Diagnostics.Tracing;

namespace Concurrency.Diagnostics;

/// <summary>
/// EventSource visible to PerfView, dotnet-trace, and EventPipe.
/// Subscribe with: <c>dotnet-trace collect --providers DotnetConcurrency-Demo</c>.
/// </summary>
[EventSource(Name = "DotnetConcurrency-Demo")]
public sealed class ConcurrencyEventSource : EventSource
{
    public static readonly ConcurrencyEventSource Log = new();

    private ConcurrencyEventSource() { }

    [Event(1, Level = EventLevel.Informational, Message = "Demo started: {0}")]
    public void DemoStarted(string demo) => WriteEvent(1, demo);

    [Event(2, Level = EventLevel.Informational, Message = "Demo finished: {0} in {1} ms")]
    public void DemoFinished(string demo, long elapsedMs) => WriteEvent(2, demo, elapsedMs);

    [Event(3, Level = EventLevel.Verbose, Message = "Step: {0}")]
    public void Step(string description) => WriteEvent(3, description);

    [Event(4, Level = EventLevel.Warning, Message = "Contention detected: {0} ({1} retries)")]
    public void Contention(string site, int retries) => WriteEvent(4, site, retries);
}
