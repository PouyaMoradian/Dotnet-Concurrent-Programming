using Chapter00.Demos;
using Concurrency.Shared;

await ConsoleLab.Run("Chapter 00 — Prerequisites: how the hardware behaves",
[
    ("Cache line size probe",                CacheLineProbe.Run),
    ("False sharing — with/without padding", FalseSharingDemo.Run),
    ("Context switch cost (ping-pong)",      ContextSwitchDemo.Run),
    ("Allocation locality observation",      LocalityDemo.Run),
    ("Branch prediction — sorted vs unsorted", BranchPredictionDemo.Run),
    ("Instruction-level parallelism",        InstructionLevelParallelismDemo.Run),
    ("Memory latency ladder (L1→L2→L3→DRAM)", MemoryLatencyLadderDemo.Run),
    ("Contended Interlocked vs sharded",     ContendedInterlockedDemo.Run),
    ("Prefetcher — sequential vs random",    PrefetchAndStrideDemo.Run),
    ("SIMD — scalar vs Vector<T>",           SimdSpeedupDemo.Run),
],
args);
