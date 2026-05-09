using Chapter09.Demos;
using Concurrency.Shared;

await ConsoleLab.Run("Chapter 09 — Channels",
[
    ("Bounded channel — backpressure",        BoundedDemo.Run),
    ("Unbounded channel — danger of memory growth", UnboundedDemo.Run),
    ("Pipeline (3-stage) with channels",      PipelineDemo.Run),
    ("Actor — single reader owning state",    ActorDemo.Run),
],
args);
