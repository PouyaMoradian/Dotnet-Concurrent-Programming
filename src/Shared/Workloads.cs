namespace Concurrency.Shared;

/// <summary>
/// Synthetic CPU-bound and IO-bound workloads that the chapters reuse so that
/// "compute-heavy" and "wait-heavy" examples are reproducible across machines.
/// </summary>
public static class Workloads
{
    /// <summary>
    /// CPU-bound workload that resists JIT elision. Returns a value the caller must observe
    /// (otherwise the JIT can lift the loop to a constant under tier-1 PGO).
    /// </summary>
    public static long Cpu(int iterations)
    {
        var x = 0L;
        for (var i = 1; i <= iterations; i++)
        {
            // A mix of integer and FP work so vectorization can't trivially fold it.
            x += i ^ (i << 1);
            x ^= (long)Math.Sqrt(i);
        }
        return x;
    }

    /// <summary>
    /// Real IO-bound delay using <see cref="Task.Delay"/>. Use this rather than
    /// <see cref="Thread.Sleep"/> in async demos — Sleep blocks the worker.
    /// </summary>
    public static Task Io(int milliseconds, CancellationToken ct = default)
        => Task.Delay(milliseconds, ct);

    /// <summary>
    /// Mixed workload: some compute, then async IO, then more compute.
    /// Useful for demonstrating async state machines and continuation hops.
    /// </summary>
    public static async Task<long> Mixed(int cpuIters, int ioMs, CancellationToken ct = default)
    {
        var pre = Cpu(cpuIters);
        await Task.Delay(ioMs, ct).ConfigureAwait(false);
        var post = Cpu(cpuIters);
        return pre + post;
    }
}
