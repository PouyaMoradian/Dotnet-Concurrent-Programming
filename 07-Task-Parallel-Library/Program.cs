using Chapter07.Demos;
using Concurrency.Shared;

await ConsoleLab.Run("Chapter 07 — Task Parallel Library",
[
    ("Parallel.For — sum with localInit/localFinally", ParallelForDemo.Run),
    ("Parallel.ForEachAsync — IO fan-out cap",         ParallelForEachAsyncDemo.Run),
    ("Task.WhenAll — exception aggregation",            WhenAllExceptionsDemo.Run),
    ("Task.WhenEach — streaming completion (.NET 9)",   WhenEachDemo.Run),
    ("ValueTask hot path",                              ValueTaskDemo.Run),
    ("Structured concurrency — cancel siblings",        StructuredConcurrencyDemo.Run),
],
args);
