using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

BenchmarkRunner.Run<AllocBench>();

[MemoryDiagnoser]
public class AllocBench
{
    private readonly int[] _data = Enumerable.Range(0, 100_000).ToArray();

    [Benchmark(Baseline = true)]
    public long ForLoop()
    {
        var s = 0L;
        for (var i = 0; i < _data.Length; i++) if ((_data[i] & 1) == 0) s += _data[i] * 2;
        return s;
    }

    [Benchmark]
    public long LinqChain()
    {
        long s = 0;
        foreach (var x in _data.Where(x => x % 2 == 0).Select(x => x * 2)) s += x;
        return s;
    }

    [Benchmark]
    public long LinqAggregate()
    {
        return _data.Where(x => x % 2 == 0).Aggregate(0L, (acc, x) => acc + x * 2);
    }

    [Benchmark]
    public long ForLoopWithSpan()
    {
        var s = 0L;
        var span = _data.AsSpan();
        for (var i = 0; i < span.Length; i++) if ((span[i] & 1) == 0) s += span[i] * 2;
        return s;
    }
}
