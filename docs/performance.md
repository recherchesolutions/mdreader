# Performance budgets

Deterministic render-pipeline budgets are enforced in CI; GUI cold start is
reported manually (too environment-sensitive to gate).

## Running

```powershell
dotnet run --project benchmarks/MdReader.Benchmarks -c Release -- --quick   # report only
dotnet run --project benchmarks/MdReader.Benchmarks -c Release -- --check   # enforce budgets.json (CI)
dotnet run --project benchmarks/MdReader.Benchmarks -c Release              # full BenchmarkDotNet suite
```

Large inputs are generated deterministically at run time (`BenchDocs.Build`) —
no multi-megabyte fixtures are checked in.

## Budgets (benchmarks/budgets.json)

Baseline measured 2026-08-01 on the project build machine (Windows Server VM);
budgets are baseline ×2 so environmental noise doesn't flake CI while real
regressions (like the O(n²) issues found during development) still fail fast.

| Metric | Baseline | Budget |
|---|---|---|
| Pipeline build | 0.1 ms | 10 ms |
| Render 100 KB | 117 ms | 300 ms |
| Render 1 MB | 1.7 s | 3.5 s |
| Render 10 MB | 53.5 s | 110 s |

"Render" is the full deterministic pipeline: parse → anchors → sanitize →
image policy → serialize. Numbers are best-of-N stopwatch runs; interpret
regressions by re-running `--quick` twice before blaming a change.

## Cold start (reported, not gated)

```powershell
tools/measure-startup.ps1   # launches the published exe, reports launch → first render
```

Baseline on this machine: ~4.5–5 s (of which ~2 s is WebView2's first
navigation — environmental; typical desktops measure far lower). Warm
single-instance handoff to a new tab: ~0.4 s.
