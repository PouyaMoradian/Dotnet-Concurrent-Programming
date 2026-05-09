using Chapter06.Demos;
using Concurrency.Shared;

await ConsoleLab.Run("Chapter 06 — Concurrent Collections",
[
    ("ConcurrentDictionary GetOrAdd race", DictGetOrAddRaceDemo.Run),
    ("Producer/consumer (queue vs channel)", ProdConsDemo.Run),
    ("ImmutableDictionary CoW vs Concurrent", ImmutableVsConcurrentDemo.Run),
    ("FrozenDictionary read perf",          FrozenDemo.Run),
],
args);
