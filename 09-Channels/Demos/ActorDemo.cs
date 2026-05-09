using System.Threading.Channels;

namespace Chapter09.Demos;

/// <summary>
/// An "actor" is a single-reader channel + an owner task that keeps state local
/// to itself. Many producers post messages; the owner serialises updates without
/// any locks. The classic shared-state problem turns into an addressing problem.
/// </summary>
internal static class ActorDemo
{
    public static async Task Run()
    {
        var actor = new CounterActor();
        var done = actor.Run();

        // Many concurrent callers — no locks anywhere.
        var clients = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < 1000; i++) await actor.Increment();
        }));
        await Task.WhenAll(clients);

        actor.Complete();
        await done;

        Console.WriteLine($"  actor counter (expected 8000): {actor.Snapshot}");
    }

    private sealed class CounterActor
    {
        private readonly Channel<Func<long, long>> _mailbox = Channel.CreateUnbounded<Func<long, long>>();
        private long _state;

        public Task Run() => Task.Run(async () =>
        {
            await foreach (var msg in _mailbox.Reader.ReadAllAsync())
                _state = msg(_state);                  // mutation always on this single task
        });

        public ValueTask Increment() => _mailbox.Writer.WriteAsync(s => s + 1);
        public void Complete() => _mailbox.Writer.Complete();
        public long Snapshot => Volatile.Read(ref _state);
    }
}
