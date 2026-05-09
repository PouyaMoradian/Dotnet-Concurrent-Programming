using Chapter10.Demos;
using Concurrency.Shared;

await ConsoleLab.Run("Chapter 10 — TPL Dataflow",
[
    ("Linear pipeline (Transform → Action)", LinearPipelineDemo.Run),
    ("Batched processing (BatchBlock)",      BatchPipelineDemo.Run),
    ("Backpressure across links",            BackpressureDemo.Run),
],
args);
