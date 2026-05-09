# 14 — Advanced Patterns

> **Layer:** application architecture
> **Reading time:** ~30 minutes
> **Prereq:** [09](../09-Channels/), [13](../13-Cancellation-and-Coordination/)

The patterns in this chapter are *architectural*. They don't introduce new primitives; they combine the ones from earlier chapters into shapes that solve common production problems.

## The map

| Pattern | Solves |
|---|---|
| [ActorModel](ActorModel/) | Confined mutable state, message-driven processing |
| [CQRS](CQRS/) | Separating commands (writes) from queries (reads) |
| [ReactiveSystems](ReactiveSystems/) | Event-driven, push-based pipelines |
| [Pipelines](Pipelines/) | Stage-by-stage processing of streams |
| [EventLoop](EventLoop/) | Single-threaded execution model with a queue |
| [Bulkheads](Bulkheads/) | Isolating failure domains |
| [CircuitBreakers](CircuitBreakers/) | Failing fast when a dependency is sick |
| [RateLimiting](RateLimiting/) | Bounding caller rate |

## The unifying theme

Every pattern here is about **shaping concurrency to limit blast radius**. Without these patterns, a single misbehaving dependency can take down a service. With them, problems stay localised.

## Run

```bash
dotnet run --project 14-Advanced-Patterns
```
