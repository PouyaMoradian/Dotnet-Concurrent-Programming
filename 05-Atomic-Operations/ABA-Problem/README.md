# The ABA problem

CAS asks "is the location still equal to my expected value?" Sometimes the answer is "yes" but for the **wrong reason** — the value was changed and changed back. CAS still succeeds; the algorithm misbehaves.

## Concrete scenario (simplified Treiber stack with pooled nodes)

```
initial:  head = X (X.next = Y)

Thread A:
  reads head = X
  reads X.next = Y
  ... preempted ...

Thread B:
  pops X       (head = Y)
  pops Y       (head = null)
  pushes new value into a recycled X (X' is the same address; X'.next = null)
  pushes X' as head (head = X', X'.next = null)

Thread A resumes:
  CAS(head, expected = X, new = Y)
  CAS sees head == X (because X' is the same address as X)  ✗
  head = Y    ← but Y was popped! head is now invalid
```

The issue: address equality ≠ logical equality. CAS used the address as identity.

## Where it actually shows up in .NET

In **fully managed** code, ABA is rare because:

1. The GC keeps a node alive as long as any thread holds a reference. Thread A's `old` reference *prevents* B from "recycling" it.
2. Allocations always return *fresh* references. The address can't legitimately repeat in the lifetime of A's reference.

ABA reappears when you opt out of those guarantees:

- **Object pooling on hot paths.** You explicitly recycle. Pool nodes have repeated identities.
- **Indices into a free list.** The free-list index is a small integer, definitely repeats.
- **`Unsafe`/native interop** with raw pointers.
- **`int`/`long` "version" fields** without the right packing.

## Mitigations

### 1. Tagged pointers (versioned CAS)

Pack `(reference, version)` into a struct. Increment `version` on every push. CAS on the struct as a whole:

```csharp
public readonly record struct Tagged(Node? Node, ulong Tag);

private Tagged _top;

public void Push(Node n)
{
    Tagged old, next;
    do
    {
        old = _top;                         // assume atomic: requires special read
        n.Next = old.Node;
        next = new Tagged(n, old.Tag + 1);
    } while (!Interlocked.CompareExchange(ref _top, next, old).Equals(old));
}
```

The tag wraps eventually but `ulong` gives you ~600 years at 1 GHz of CASes. Acceptable.

`Interlocked.CompareExchange(ref struct)` requires the struct fit in 8 or 16 bytes for the CPU intrinsic; the JIT picks `cmpxchg8b` or `cmpxchg16b` on x86 / `LDAXP+STLXP` on ARM64.

### 2. Hazard pointers

Each thread publishes the references it's currently inspecting in a per-thread "hazard" slot. Reclamation waits until no hazard slot points at a given address. Common in C/C++ lock-free code, rare in C# because the GC is doing this for you.

### 3. Epoch-based reclamation

Threads enter "epochs"; a node is only recycled when no thread is in an epoch that could see it. Used by Crossbeam in Rust; rare in C# code.

### 4. Just don't pool

In managed C#, *not* pooling nodes — letting the GC handle reclamation — sidesteps ABA entirely. The cost is allocation pressure, which on Gen 0 is much cheaper than the bookkeeping of versioned CAS.

## Practical advice

If you find yourself worrying about ABA, ask: **am I optimising prematurely?** The BCL's `ConcurrentQueue<T>`, `ConcurrentStack<T>`, and `ConcurrentDictionary<K,V>` are lock-free where it matters and ABA-correct. Your custom queue is unlikely to outperform them. Use the BCL types unless your benchmark unambiguously says otherwise.
