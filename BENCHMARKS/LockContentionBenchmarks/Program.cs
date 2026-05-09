using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

BenchmarkRunner.Run<LockBench>();

[MemoryDiagnoser]
public class LockBench
{
    [Params(2, 4, 8)] public int Threads;
    private const int IterationsPerThread = 200_000;

    [Benchmark(Baseline = true)]
    public async Task<long> LockObject()
    {
        var sync = new object();
        long counter = 0;
        await Parallel.ForEachAsync(Enumerable.Range(0, Threads), async (_, _) =>
        {
            await Task.Yield();
            for (var i = 0; i < IterationsPerThread; i++)
                lock (sync) counter++;
        });
        return counter;
    }

#if NET9_0_OR_GREATER
    [Benchmark]
    public async Task<long> SystemThreadingLock()
    {
        var sync = new System.Threading.Lock();
        long counter = 0;
        await Parallel.ForEachAsync(Enumerable.Range(0, Threads), async (_, _) =>
        {
            await Task.Yield();
            for (var i = 0; i < IterationsPerThread; i++)
                lock (sync) counter++;
        });
        return counter;
    }
#endif

    [Benchmark]
    public async Task<long> Interlocked()
    {
        long counter = 0;
        await Parallel.ForEachAsync(Enumerable.Range(0, Threads), async (_, _) =>
        {
            await Task.Yield();
            for (var i = 0; i < IterationsPerThread; i++)
                System.Threading.Interlocked.Increment(ref counter);
        });
        return counter;
    }
}
