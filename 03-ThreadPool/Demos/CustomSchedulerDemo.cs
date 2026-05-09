using System.Collections.Concurrent;

namespace Chapter03.Demos;

/// <summary>
/// A minimal sequential <see cref="TaskScheduler"/>: tasks queued to it run one at a time,
/// in FIFO order, on whatever pool thread happens to be free. Useful when you want
/// exclusive access to a non-thread-safe resource without using locks.
/// </summary>
internal sealed class SequentialScheduler : TaskScheduler
{
    private readonly ConcurrentQueue<Task> _queue = new();
    private int _running; // 0 or 1; CAS controls who pumps.

    public override int MaximumConcurrencyLevel => 1;

    protected override void QueueTask(Task task)
    {
        _queue.Enqueue(task);
        TryPump();
    }

    private void TryPump()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        ThreadPool.UnsafeQueueUserWorkItem(static state =>
        {
            var s = (SequentialScheduler)state!;
            try
            {
                while (s._queue.TryDequeue(out var t))
                    s.TryExecuteTask(t);
            }
            finally
            {
                Volatile.Write(ref s._running, 0);
                if (!s._queue.IsEmpty) s.TryPump();
            }
        }, this);
    }

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;
    protected override IEnumerable<Task> GetScheduledTasks() => _queue.ToArray();
}

internal static class CustomSchedulerDemo
{
    public static async Task Run()
    {
        var scheduler = new SequentialScheduler();
        var factory = new TaskFactory(scheduler);

        var orderObserved = new System.Collections.Concurrent.ConcurrentQueue<int>();
        var tasks = Enumerable.Range(0, 8).Select(i =>
            factory.StartNew(() =>
            {
                orderObserved.Enqueue(i);
                Thread.Sleep(20);
            })).ToArray();
        await Task.WhenAll(tasks);

        Console.WriteLine($"  Order observed: {string.Join(",", orderObserved)}");
        Console.WriteLine("  All tasks ran sequentially on (possibly different) pool threads.");
    }
}
