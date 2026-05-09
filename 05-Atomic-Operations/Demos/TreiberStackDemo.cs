namespace Chapter05.Demos;

/// <summary>
/// A lock-free LIFO stack via the Treiber pattern. Each Push allocates a node
/// (which is *why* Treiber is GC-friendly: the GC handles ABA for us, since
/// the Node reference itself uniquely identifies a version).
/// </summary>
internal sealed class TreiberStack<T>
{
    private sealed class Node { public T Value = default!; public Node? Next; }
    private Node? _head;

    public void Push(T value)
    {
        var n = new Node { Value = value };
        Node? old;
        do
        {
            old = Volatile.Read(ref _head);
            n.Next = old;
        } while (Interlocked.CompareExchange(ref _head, n, old) != old);
    }

    public bool TryPop(out T value)
    {
        Node? old;
        Node? next;
        do
        {
            old = Volatile.Read(ref _head);
            if (old is null) { value = default!; return false; }
            next = old.Next;
        } while (Interlocked.CompareExchange(ref _head, next, old) != old);
        value = old.Value;
        return true;
    }
}

internal static class TreiberStackDemo
{
    public static async Task Run()
    {
        var stack = new TreiberStack<int>();
        const int producers = 4, consumers = 4, items = 100_000;

        var produced = 0;
        var consumed = 0;
        var totalIn = producers * items;

        var pTasks = Enumerable.Range(0, producers).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < items; i++) { stack.Push(i); Interlocked.Increment(ref produced); }
        }));
        var cTasks = Enumerable.Range(0, consumers).Select(_ => Task.Run(() =>
        {
            while (Volatile.Read(ref consumed) < totalIn)
                if (stack.TryPop(out _)) Interlocked.Increment(ref consumed);
        }));

        await Task.WhenAll(pTasks.Concat(cTasks));

        Console.WriteLine($"  produced: {produced:N0}, consumed: {consumed:N0}");
        Console.WriteLine($"  ConcurrentStack<T> in the BCL is essentially this, with attention to detail.");
    }
}
