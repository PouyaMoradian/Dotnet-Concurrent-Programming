# Academic papers — the classics

These are the papers that shaped how we think about concurrency. Each is short; each rewards a careful read.

## Memory models

- **Leslie Lamport — *How to Make a Multiprocessor Computer That Correctly Executes Multiprocess Programs*** (1979). The paper that defined sequential consistency.
- **Sarita Adve & Mark Hill — *Weak Ordering — A New Definition*** (1990). Defined the relaxed memory model framework.
- **Hans Boehm — *Threads Cannot Be Implemented as a Library*** (2005). Why memory models must be language-level.
- **Sevcik & Aspinall — *On Validity of Program Transformations in the Java Memory Model*** (2008). Why the memory model affects compilers.

## Synchronisation primitives

- **Edsger Dijkstra — *Cooperating Sequential Processes*** (1965). Introduces semaphores.
- **Tony Hoare — *Monitors: An Operating System Structuring Concept*** (1974). The Monitor pattern.
- **Lampson & Redell — *Experience with Processes and Monitors in Mesa*** (1980). Why most modern monitors look like Mesa, not Hoare.

## Lock-free data structures

- **Maged Michael & Michael Scott — *Simple, Fast, and Practical Non-Blocking and Blocking Concurrent Queue Algorithms*** (1996). The MS queue. Influential for `ConcurrentQueue<T>`.
- **R. Kent Treiber — *Systems Programming: Coping with Parallelism*** (IBM, 1986). The Treiber stack.
- **Maurice Herlihy — *Wait-Free Synchronization*** (1991). Defined the levels: lock-free / wait-free / obstruction-free.

## Memory reclamation

- **Maged Michael — *Hazard Pointers: Safe Memory Reclamation for Lock-Free Objects*** (2004). Hazard pointers: the canonical safe reclamation scheme.
- **Fraser & Harris — *Concurrent Programming Without Locks*** (2007). Multi-CAS, software transactional memory.

## Schedulers and work-stealing

- **Robert Blumofe & Charles Leiserson — *Scheduling Multithreaded Computations by Work Stealing*** (1999). The Cilk paper. The .NET ThreadPool's work-stealing is in this lineage.

## Hardware

- **Ulrich Drepper — *What Every Programmer Should Know About Memory*** (2007). Free PDF. Cache lines, NUMA, prefetching, memory ordering — from a Linux kernel perspective but universally applicable.

## How to read them

These papers are dense but short. A useful approach:

1. **Read the abstract, then the conclusions.** Decide if it's worth your time.
2. **Skim the introduction.** Get the high-level claim.
3. **Read the algorithm or proof sketch.** Don't grind through full proofs unless you need to.
4. **Re-read once a year.** Each pass reveals more.

The Treiber, Michael-Scott, and Herlihy wait-free papers have shaped `System.Collections.Concurrent` directly. Reading them is reading the BCL's intellectual history.
