using System.Collections.Frozen;
using System.Diagnostics;

namespace Chapter06.Demos;

internal static class FrozenDemo
{
    public static Task Run()
    {
        const int n = 10_000;
        const int reads = 10_000_000;

        var keys = Enumerable.Range(0, n).Select(i => "key_" + i).ToArray();
        var dict = keys.ToDictionary(k => k, k => k.Length);
        var frozen = dict.ToFrozenDictionary();

        var sw = Stopwatch.StartNew();
        long s1 = 0;
        for (var i = 0; i < reads; i++) s1 += dict[keys[i % n]];
        sw.Stop();
        Console.WriteLine($"  Dictionary<string,int>:    {sw.ElapsedMilliseconds} ms (sum={s1})");

        sw.Restart();
        long s2 = 0;
        for (var i = 0; i < reads; i++) s2 += frozen[keys[i % n]];
        sw.Stop();
        Console.WriteLine($"  FrozenDictionary<string,int>: {sw.ElapsedMilliseconds} ms (sum={s2})");

        Console.WriteLine();
        Console.WriteLine("  Frozen does extra work at construction (perfect-hash-like lookups) and amortises it");
        Console.WriteLine("  over reads. Best for build-once + read-many maps such as routing tables, settings.");
        return Task.CompletedTask;
    }
}
