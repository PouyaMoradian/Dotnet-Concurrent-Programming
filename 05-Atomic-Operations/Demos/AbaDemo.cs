namespace Chapter05.Demos;

/// <summary>
/// Demonstrates the *idea* of ABA on a deliberately fragile stack
/// that reuses node objects via a pool — recreating the exact pre-condition
/// that managed code typically avoids.
/// </summary>
internal static class AbaDemo
{
    public static async Task Run()
    {
        Console.WriteLine("  ABA: Thread A reads head=X, gets preempted.");
        Console.WriteLine("       Thread B pops X, pushes Y, then pushes X (recycled node) back.");
        Console.WriteLine("       Thread A's CAS sees head==X again and 'succeeds' — but Next is now wrong.");
        Console.WriteLine();
        Console.WriteLine("  In *managed* .NET this is rare because:");
        Console.WriteLine("    1. Allocations create fresh references; the GC tracks them.");
        Console.WriteLine("    2. A 'recycled' node would be GC'd while no one holds it; A still holds the old reference.");
        Console.WriteLine();
        Console.WriteLine("  ABA reappears when:");
        Console.WriteLine("    a) You pool nodes (object pooling for hot paths).");
        Console.WriteLine("    b) You CAS over an *index* into a free list rather than a reference.");
        Console.WriteLine("    c) You unsafely cast values that can repeat.");
        Console.WriteLine();
        Console.WriteLine("  Mitigation: tagged pointers (CAS on a 16-byte struct {ref, version}),");
        Console.WriteLine("  or hazard pointers / epoch reclamation in unmanaged-style designs.");
        Console.WriteLine();

        // This demo is illustrative only; we don't reproduce a real ABA — that requires careful
        // unsafe code or P/Invoke that the BCL avoids. See TreiberStack<T> in the same folder for
        // the GC-protected variant.
        await Task.Yield();
    }
}
