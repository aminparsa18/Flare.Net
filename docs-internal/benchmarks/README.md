# Benchmark runs

One folder per run of `scripts/bench.sh`, named `<yyyy-mm-dd>` (`-2`, `-3`, ... for a second run that
day). Each folder is a dated record that later runs are compared against, so it is never edited
after the fact. A newer run goes in its own folder. How to run the benchmarks and add new ones is
in [src/Flare.Benchmarks/README.md](../../src/Flare.Benchmarks/README.md). What the numbers *mean*
belongs in an investigation under [investigations/](../investigations/), which links to the run it
draws on.

| File | Commit? | What |
|---|---|---|
| `report.html` | yes | self-contained report: open it in a browser, works offline |
| `results.json` | yes | normalized results, the input the next run is compared against |
| `env.json` | yes | machine, OS, .NET/Node/V8 versions, git commit, suites, quick mode |
| `raw/sizes.json` | yes | fixture manifest: every scenario's byte size per format and codec |
| `raw/dotnet/*-report-brief.json`, `*-report.csv` | yes | BenchmarkDotNet output |
| `raw/node/*.json` | yes | tinybench output |
| `artifacts/bench/` (repo root) | no, gitignored | full BenchmarkDotNet artifacts and the exported fixture bytes |

Commit only full-accuracy runs. A `--quick` run is labelled as one in its report and is for
smoke-testing changes to the benchmarks themselves.

## Runs

- [2026-11-04](2026-11-04/report.html): first run. Its findings are in
  [investigations/serialization-end-to-end-benchmark.md](../investigations/serialization-end-to-end-benchmark.md).
