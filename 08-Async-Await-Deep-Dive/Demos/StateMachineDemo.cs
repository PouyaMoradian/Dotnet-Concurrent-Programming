using Concurrency.Shared;

namespace Chapter08.Demos;

internal static class StateMachineDemo
{
    public static async Task Run()
    {
        Console.WriteLine($"  before await: {ThreadInfo.Describe()}");
        await Task.Delay(20);
        Console.WriteLine($"  after  await: {ThreadInfo.Describe()}");
        await Task.Yield();
        Console.WriteLine($"  after yield : {ThreadInfo.Describe()}");
        Console.WriteLine();
        Console.WriteLine("  No SynchronizationContext on console host — continuations resume on the pool.");
        Console.WriteLine("  ManagedThreadId frequently changes across awaits.");
    }
}
