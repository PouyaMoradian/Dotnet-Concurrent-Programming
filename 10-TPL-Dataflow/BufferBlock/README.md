# BufferBlock&lt;T&gt;

The simplest block: a typed FIFO queue with `Post`/`SendAsync` (input) and `LinkTo`/`Receive`/`OutputAvailableAsync` (output). Used as a buffering point between asymmetric stages.

```csharp
var buffer = new BufferBlock<int>(new DataflowBlockOptions { BoundedCapacity = 1000 });

await buffer.SendAsync(42);     // honours BoundedCapacity
var x = await buffer.ReceiveAsync();
```

## When you reach for it

- You want a **named queue** between stages with explicit type and bound.
- You need to **fan-in from multiple producers, fan-out to multiple consumers** without writing the routing yourself.
- You want the consumer side to use `OutputAvailableAsync` + `TryReceive` (rare; the `LinkTo` -> `ActionBlock` pattern is more common).

## Linking

```csharp
buffer.LinkTo(consumer, new DataflowLinkOptions { PropagateCompletion = true });
```

Multiple consumers can link to the same buffer; items go to whichever takes them first. Use a predicate overload for routing:

```csharp
buffer.LinkTo(highPrioritySink, new DataflowLinkOptions { Append = true }, x => x.IsHighPriority);
buffer.LinkTo(lowPrioritySink, new DataflowLinkOptions { Append = true });   // default
```

Predicate routing is one of Dataflow's most useful features — hard to do as cleanly in `Channel<T>`.

## Comparison with `Channel<T>`

| | `BufferBlock<T>` | `Channel<T>` |
|---|---|---|
| Async-native | partial | yes |
| Predicate-routed links | yes | no (write yourself) |
| Bounded backpressure | yes | yes |
| Per-stage parallelism | n/a (BufferBlock is just a queue) | n/a (just a queue) |
| Allocation per item | higher (`SendAsync` returns Task) | lower |

If you only need a queue, `Channel<T>` wins. `BufferBlock<T>` shines when paired with predicate-`LinkTo` for typed routing.
