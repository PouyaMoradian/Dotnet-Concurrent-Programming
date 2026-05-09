using System.Diagnostics;

namespace Concurrency.Shared;

/// <summary>
/// Tiny harness used by every chapter sample so the executable surface is uniform:
/// each chapter's <c>Program.cs</c> calls <see cref="Run"/> with a list of named demos.
/// </summary>
public static class ConsoleLab
{
    public static async Task Run(string title, IReadOnlyList<(string Name, Func<Task> Demo)> demos, string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        PrintBanner(title);

        if (args is { Length: > 0 } && int.TryParse(args[0], out var idx) && idx >= 0 && idx < demos.Count)
        {
            await RunOne(demos[idx]);
            return;
        }

        // Interactive picker.
        for (var i = 0; i < demos.Count; i++)
        {
            Console.WriteLine($"  [{i,2}] {demos[i].Name}");
        }
        Console.WriteLine($"  [ a] all");
        Console.Write("\n> ");
        var input = Console.ReadLine()?.Trim();

        if (string.Equals(input, "a", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var demo in demos) await RunOne(demo);
        }
        else if (int.TryParse(input, out var pick) && pick >= 0 && pick < demos.Count)
        {
            await RunOne(demos[pick]);
        }
    }

    private static async Task RunOne((string Name, Func<Task> Demo) d)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"── {d.Name} ──");
        Console.ResetColor();

        var sw = Stopwatch.StartNew();
        try
        {
            await d.Demo();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"FAILED: {ex.GetType().Name}: {ex.Message}");
            Console.ResetColor();
        }
        sw.Stop();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"   ({sw.ElapsedMilliseconds} ms)");
        Console.ResetColor();
    }

    private static void PrintBanner(string title)
    {
        var line = new string('═', Math.Max(40, title.Length + 4));
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(line);
        Console.WriteLine($"  {title}");
        Console.WriteLine(line);
        Console.ResetColor();
        Console.WriteLine($"  Process: {Environment.ProcessId}   Cores: {Environment.ProcessorCount}   GC: {(System.Runtime.GCSettings.IsServerGC ? "Server" : "Workstation")}   .NET: {Environment.Version}");
        Console.WriteLine();
    }
}
