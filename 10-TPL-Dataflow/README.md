# 10 — TPL Dataflow

> **Layer:** BCL extension (`System.Threading.Tasks.Dataflow`)
> **Reading time:** ~30 minutes
> **Prereq:** [09-Channels](../09-Channels/)

TPL Dataflow is a higher-level pipeline framework. Where `Channel<T>` gives you a pipe, Dataflow gives you a *typed graph* of blocks — each block has its own concurrency policy, its own bounded buffer, and its own composition operators.

It's older than `Channel<T>` (TPL Dataflow shipped in 2012). It's still useful when:

- You want **declarative graph composition** (`source.LinkTo(transform).LinkTo(sink)`).
- You need **batching/joining** (`BatchBlock`, `JoinBlock`) without writing it yourself.
- You want **per-block parallelism** (`MaxDegreeOfParallelism = 4` on this stage, `1` on that one).

For *simple* producer-consumer, `Channel<T>` is smaller, faster, and async-native. Reach for Dataflow when the graph or the batching gets complex.

## In-chapter folders

| Folder | Topic |
|---|---|
| [BufferBlock](BufferBlock/) | Pure FIFO buffer; the basic in/out block |
| [TransformBlock](TransformBlock/) | One-in, one-out transform with concurrency |
| [BatchBlock](BatchBlock/) | Aggregate N items before emitting a batch |
| [ActionBlock](ActionBlock/) | Terminal block — runs an action per item |
| [Backpressure](Backpressure/) | `BoundedCapacity` and how it composes through links |
| [ProductionPipelines](ProductionPipelines/) | A real telemetry-shaped example |

## The minimal pipeline

```csharp
var fetch = new TransformBlock<Uri, string>(
    async u => await client.GetStringAsync(u),
    new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 4, BoundedCapacity = 100 });

var parse = new TransformBlock<string, Document>(
    Parse,
    new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, BoundedCapacity = 100 });

var sink = new ActionBlock<Document>(
    Save,
    new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1, BoundedCapacity = 100 });

fetch.LinkTo(parse, new DataflowLinkOptions { PropagateCompletion = true });
parse.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });

// feed
foreach (var url in urls) await fetch.SendAsync(url);
fetch.Complete();
await sink.Completion;
```

`PropagateCompletion = true` is essential — without it, completing `fetch` doesn't tell `parse` to drain and `sink` to finish. The whole graph hangs.

## When Dataflow really shines

- **Different concurrency at different stages.** Network IO at 32-way fan-out; CPU work at `Environment.ProcessorCount`; DB writes at 1 (transactional).
- **Per-stage bounding.** A slow downstream propagates pressure upstream automatically.
- **Joining**. `JoinBlock<T1, T2>` waits for matched pairs from two upstreams; useful for correlation.

## When `Channel<T>` is cleaner

- Two stages, single concurrency, simple types. Dataflow's ceremony costs more than it saves.
- You want full async surface (`IAsyncEnumerable<T>`). Channels integrate with `await foreach` directly; Dataflow uses `OutputAvailableAsync` + `TryReceive`.

## Run

```bash
dotnet run --project 10-TPL-Dataflow
```
