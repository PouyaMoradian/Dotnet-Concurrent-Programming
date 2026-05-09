# Repository architecture

This is a *teaching* repository, but it is also a real .NET solution. This document explains how the code is organised so the two roles never collide.

## Layers

```
                  ┌──────────────────────────────────────┐
                  │   Chapters 00–19 (numbered folders)  │   ← curriculum
                  │   each is a runnable .NET project    │
                  └────────────────┬─────────────────────┘
                                   │ project ref
                  ┌────────────────▼─────────────────────┐
                  │   src/Shared, src/Diagnostics        │   ← common helpers
                  │   ConsoleLab, ThreadInfo, EventSource│
                  └────────────────┬─────────────────────┘
                                   │
                  ┌────────────────▼─────────────────────┐
                  │   .NET 10 BCL (System.Threading,     │   ← stdlib
                  │   System.Threading.Tasks, .Channels, │
                  │   .RateLimiting, .Tasks.Dataflow)    │
                  └──────────────────────────────────────┘

          BENCHMARKS/* — independent of chapters; consume only Shared.
          tests/*      — own projects; reference what they test.
```

## Why "one project per chapter"?

Three reasons:

1. **Self-contained reading.** Each chapter compiles in isolation; the reader can navigate just the folder they care about.
2. **Different MSBuild needs.** Chapter 12 (memory model) enables `<AllowUnsafeBlocks>`. Chapter 16 (NativeAOT) enables `<PublishAot>`. Per-project settings stay local.
3. **Discoverability.** `dotnet run --project 08-Async-Await-Deep-Dive` is a more obvious onramp than navigating into a giant binary.

## Build conventions

- **Central Package Management** (`Directory.Packages.props`) — every version is in one file.
- **`Directory.Build.props`** — every chapter inherits `LangVersion=latest`, `Nullable=enable`, server GC, tiered PGO. Benchmarks inherit but override where needed.
- **Solution folders** — `Chapters`, `Benchmarks`, `Shared`. The `.sln` keeps these grouped in IDEs.

## Reading order

The numbering is not arbitrary. Each chapter assumes you've read the previous ones:

- Hardware → OS → Runtime → BCL → Patterns → Production → Pitfalls.
- Skipping ahead is fine if you already have the prerequisites; the README of each chapter lists what it depends on at the top.
