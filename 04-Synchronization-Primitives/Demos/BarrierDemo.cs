namespace Chapter04.Demos;

/// <summary>
/// A Barrier coordinates phased computation. N threads execute phase 1,
/// rendezvous at the barrier, then all proceed to phase 2 simultaneously.
/// Demonstrated with a tiny iterative simulation.
/// </summary>
internal static class BarrierDemo
{
    public static async Task Run()
    {
        const int participants = 4;
        const int phases = 3;

        using var barrier = new Barrier(participants, b =>
        {
            Console.WriteLine($"   --- phase {b.CurrentPhaseNumber} complete ---");
        });

        var tasks = Enumerable.Range(0, participants).Select(id => Task.Run(() =>
        {
            for (var p = 0; p < phases; p++)
            {
                Console.WriteLine($"   worker {id}: doing phase {p}");
                Thread.Sleep(50 * (id + 1));            // workers proceed at different speeds
                barrier.SignalAndWait();                // wait until everyone is here
            }
        }));

        await Task.WhenAll(tasks);
    }
}
