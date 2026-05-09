using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

BenchmarkRunner.Run<AsyncBench>();

[MemoryDiagnoser]
public class AsyncBench
{
    // Cache hit on every call → all three should sync-complete.
    private static readonly Dictionary<int, int> _cache = Enumerable.Range(0, 100).ToDictionary(i => i, i => i);

    [Benchmark(Baseline = true)]
    public async Task<int> TaskOfInt_SyncComplete()
    {
        var v = await GetTaskAsync(42);
        return v + 1;
    }

    [Benchmark]
    public async ValueTask<int> ValueTaskOfInt_SyncComplete()
    {
        var v = await GetValueTaskAsync(42);
        return v + 1;
    }

    [Benchmark]
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> PooledValueTaskOfInt_SyncComplete()
    {
        var v = await GetValueTaskAsync(42);
        return v + 1;
    }

    private static Task<int> GetTaskAsync(int key) =>
        _cache.TryGetValue(key, out var v) ? Task.FromResult(v) : SlowTask(key);

    private static ValueTask<int> GetValueTaskAsync(int key) =>
        _cache.TryGetValue(key, out var v) ? new ValueTask<int>(v) : new ValueTask<int>(SlowTask(key));

    private static async Task<int> SlowTask(int key) { await Task.Yield(); return key; }
}
