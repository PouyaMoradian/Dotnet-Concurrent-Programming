using System.Diagnostics;
using Concurrency.Shared;

await ConsoleLab.Run("Chapter 02 — The OS Threading Model",
[
    ("Show current thread", () =>
    {
        Console.WriteLine($"  {ThreadInfo.Describe()}");
        Console.WriteLine($"  runtime: {ThreadInfo.DescribeRuntime()}");
        return Task.CompletedTask;
    }),
    ("Process affinity", () =>
    {
        using var p = Process.GetCurrentProcess();
        Console.WriteLine($"  current ProcessorAffinity: 0x{p.ProcessorAffinity.ToInt64():X}");
        Console.WriteLine($"  cores visible:             {Environment.ProcessorCount}");
        return Task.CompletedTask;
    }),
    ("Quantum probe (sleep granularity)", async () =>
    {
        for (var i = 0; i < 5; i++)
        {
            var sw = Stopwatch.StartNew();
            await Task.Delay(1);
            sw.Stop();
            Console.WriteLine($"  Task.Delay(1) actually waited {sw.Elapsed.TotalMilliseconds:F2} ms");
        }
    }),
],
args);
