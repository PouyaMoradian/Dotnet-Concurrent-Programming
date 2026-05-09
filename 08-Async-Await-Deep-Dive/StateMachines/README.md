# State machines — what `async` actually emits

The C# compiler rewrites every `async` method into a state machine. This is not metaphorical — open the IL and you'll find a `[CompilerGenerated]` struct (or class in DEBUG) per async method, with an integer `_state` field and a `MoveNext()` method that switches on it.

## A worked example

Source:

```csharp
public async Task<int> AddAsync(int a)
{
    var b = await GetBAsync();
    return a + b;
}
```

The compiler emits, roughly:

```csharp
[AsyncStateMachine(typeof(<AddAsync>d__0))]
[DebuggerStepThrough]
public Task<int> AddAsync(int a)
{
    var sm = new <AddAsync>d__0();
    sm.<>t__builder = AsyncTaskMethodBuilder<int>.Create();
    sm.<>4__this = this;
    sm.a = a;
    sm.<>1__state = -1;
    sm.<>t__builder.Start(ref sm);          // calls MoveNext synchronously up to first await
    return sm.<>t__builder.Task;
}

[CompilerGenerated]
private struct <AddAsync>d__0 : IAsyncStateMachine
{
    public int <>1__state;
    public AsyncTaskMethodBuilder<int> <>t__builder;
    public int a;
    public int <b>5__1;
    public TaskAwaiter<int> <>u__1;
    // also: <>4__this  (the captured 'this')

    public void MoveNext()
    {
        int result;
        try
        {
            int state = <>1__state;
            TaskAwaiter<int> awaiter;
            switch (state)
            {
                case -1:                    // first call
                    awaiter = GetBAsync().GetAwaiter();
                    if (!awaiter.IsCompleted)
                    {
                        <>1__state = 0;
                        <>u__1 = awaiter;
                        <>t__builder.AwaitUnsafeOnCompleted(ref awaiter, ref this);
                        return;             // suspended; MoveNext will be called again later
                    }
                    goto label_continuation;

                case 0:                     // resumed
                    awaiter = <>u__1;
                    <>u__1 = default;
                    <>1__state = -1;
                    goto label_continuation;
            }

        label_continuation:
            <b>5__1 = awaiter.GetResult();   // throws if the awaited task faulted
            result = a + <b>5__1;
        }
        catch (Exception ex)
        {
            <>1__state = -2;
            <>t__builder.SetException(ex);
            return;
        }
        <>1__state = -2;
        <>t__builder.SetResult(result);
    }

    public void SetStateMachine(IAsyncStateMachine machine) => <>t__builder.SetStateMachine(machine);
}
```

## Things to take from this

1. **The state machine is a struct.** First call to `MoveNext` (via `Start`) runs synchronously until the first incomplete await. If the method *never* awaits an incomplete task, the struct never escapes the stack — zero heap allocation.
2. **On first incomplete await, the struct is boxed** (in DEBUG it's a class from the start; in RELEASE it's boxed lazily). The boxed object is what holds the continuation.
3. **`AwaitUnsafeOnCompleted`** registers the continuation. It does *not* capture `ExecutionContext` (that's done elsewhere on the box); the "Unsafe" refers to context-flow.
4. **`SetException` / `SetResult`** complete the surface `Task<T>`. Continuations awaiting *that* task are then scheduled (on captured `SynchronizationContext` if any, otherwise on whatever thread is calling `SetResult`/`SetException`).

## "Async hop" — what every await may cost

Each await on an incomplete task involves:

- A box of the state machine (one per call that suspends; or pooled with `[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]`).
- An `Action` delegate to call back `MoveNext` (cached on the box).
- An `ExecutionContext` capture (unless suppressed).
- A queue insertion when the awaiter completes.

Total: ~a couple hundred ns of overhead per `await` that genuinely suspends. Awaits that complete synchronously cost essentially nothing.

## Hand-rolling state machines (don't, usually)

You can write your own awaitables (anything with `GetAwaiter()` returning a type with `IsCompleted`, `OnCompleted`, `GetResult`). The most common reason: pooled awaiters for hot paths (see [AllocationFreeAsync](../AllocationFreeAsync/)). For ordinary code, the compiler does this better than you would.

## See it for real

```bash
# View the IL
ildasm bin/Debug/net10.0/Chapter08.AsyncAwait.dll

# Or use sharplab.io for a quick C# → state machine view
```

Or in this project, set `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` on the chapter `.csproj` to dump the generated source.
