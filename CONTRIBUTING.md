# Contributing

Thanks for considering a contribution. This repo is held to a higher bar than typical "samples" projects, because misinformation about concurrency is the entire reason it exists.

## Ground rules

1. **Every claim must be reproducible.** Any perf assertion needs a corresponding benchmark in `BENCHMARKS/`. Any race claim needs a stress test in `tests/RaceConditionTests/`.
2. **Code must compile and run on .NET 10** with `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
3. **Prefer measurement to opinion.** "X is faster than Y" is acceptable iff a benchmark in this repo shows it on at least two distinct hardware profiles.
4. **No copy-pasted Stack Overflow code.** The bar is "explain why" — not "this seems to work."

## How to add a new section / fix an error

1. Open an issue first if it's a structural change.
2. Fork, branch, edit.
3. Add or update the affected `README.md`, the `.csproj` (if a new code sample is needed), and the relevant benchmark.
4. Run `dotnet format` and `dotnet build -c Release` — both must be clean.
5. PR with:
   - **Why** the change is needed
   - **What** changed
   - **How** you verified it (tests, benchmarks, repros)

## Style

- Markdown: 100-column soft wrap, fenced code blocks always tagged with `csharp`/`bash`/`text`.
- C#: file-scoped namespaces, `using` directives sorted, no unused symbols, prefer `var` only when the RHS is obviously typed.
- Comments: only when the *why* is non-obvious. Don't restate what the code does.

## Commit messages

Conventional Commits format: `feat(09-Channels): add SingleReader/SingleWriter benchmark`.

## Code of Conduct

Be respectful. Disagreements about concurrency are expected and welcome — the goal is correctness, not consensus.
