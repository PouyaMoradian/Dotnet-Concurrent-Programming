namespace Chapter01.Demos;

/// <summary>
/// Visualises that an <c>async</c> method can run on a different thread on every line.
/// Before each <c>await</c>, we record the managed thread id; after the await, we record
/// the new one. Often they differ — that's the whole point of async on a context-less host.
/// </summary>
internal static class ThreadHoppingDemo
{
    public static async Task Run()
    {
        Console.WriteLine("  Tracing thread ids around await points:");
        Console.WriteLine();

        var seen = new HashSet<int>();
        void Note(string where)
        {
            var id = Environment.CurrentManagedThreadId;
            seen.Add(id);
            Console.WriteLine($"    {where,-30}  managedId = {id,3}   pool = {(Thread.CurrentThread.IsThreadPoolThread ? "Y" : "N")}");
        }

        Note("entry");
        await Task.Delay(20);
        Note("after first Task.Delay");
        await Task.Delay(20);
        Note("after second Task.Delay");
        await Task.Yield();
        Note("after Task.Yield");
        await Task.Run(() => { });
        Note("after Task.Run no-op");

        Console.WriteLine();
        Console.WriteLine($"  distinct threads observed in this method: {seen.Count}");
        Console.WriteLine();
        Console.WriteLine("  On a context-free host (console, ASP.NET Core), each await is free to resume");
        Console.WriteLine("  on a different pool thread. Do NOT keep thread-affine state (locks, ThreadLocal,");
        Console.WriteLine("  Win32 handles) across an await boundary unless you've explicitly handled it.");
    }
}
