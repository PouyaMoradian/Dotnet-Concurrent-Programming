using System.Collections.Concurrent;

namespace Chapter06.Demos;

internal static class DictGetOrAddRaceDemo
{
    public static async Task Run()
    {
        var factoryInvocations = 0;
        var dict = new ConcurrentDictionary<string, int>();

        // Several threads race to GetOrAdd the same key.
        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            dict.GetOrAdd("the-key", _ =>
            {
                Interlocked.Increment(ref factoryInvocations);
                Thread.SpinWait(100_000);
                return 42;
            });
        })));

        Console.WriteLine($"  GetOrAdd factory invocations: {factoryInvocations}");
        Console.WriteLine($"  (1 is the typical case; 2-3 happens under contention; only one wins.)");

        // Wrap with Lazy<T> to make double-creation harmless and cheap.
        var lazyDict = new ConcurrentDictionary<string, Lazy<int>>();
        var lazyFactoryInvocations = 0;
        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            _ = lazyDict.GetOrAdd("the-key", _ => new Lazy<int>(() =>
            {
                Interlocked.Increment(ref lazyFactoryInvocations);
                Thread.SpinWait(100_000);
                return 42;
            }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        })));

        Console.WriteLine($"  Lazy-wrapped invocations:     {lazyFactoryInvocations}  (always 1)");
    }
}
