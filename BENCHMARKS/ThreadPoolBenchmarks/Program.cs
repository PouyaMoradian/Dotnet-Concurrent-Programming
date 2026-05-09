using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

BenchmarkRunner.Run<ThreadPoolBench>();

[MemoryDiagnoser]
public class ThreadPoolBench
{
    private readonly Action _emptyAction = static () => { };
    private readonly WaitCallback _wcb = static _ => { };

    [Benchmark(Baseline = true)]
    public async Task TaskRun()
    {
        await Task.Run(_emptyAction);
    }

    [Benchmark]
    public Task QueueUserWorkItem()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ThreadPool.QueueUserWorkItem(static state => ((TaskCompletionSource<bool>)state!).SetResult(true), tcs);
        return tcs.Task;
    }

    [Benchmark]
    public Task UnsafeQueueUserWorkItem()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ThreadPool.UnsafeQueueUserWorkItem(static state => ((TaskCompletionSource<bool>)state!).SetResult(true), tcs, preferLocal: false);
        return tcs.Task;
    }
}
