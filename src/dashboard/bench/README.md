# Dashboard benchmarks (tinybench, Node/V8)

This folder is the TypeScript half of Flare's serialization benchmarks. The setup, the suites, the
scenarios and how to add more are all documented in
[src/Flare.Benchmarks/README.md](../../Flare.Benchmarks/README.md), and `scripts/bench.sh` runs
everything.

```
component/   decode, decompress, request-encode: one operation each
workflow/    client-stage: bytes → decompress → decode → UI types
support/     fixture loading, the per-format client path, the shared runner, and a
             `$app/env/public` stub so the real src/lib/*-api.ts mappers can be imported
```

To run one suite on its own, against fixtures exported by `scripts/bench.sh` or
`dotnet run -c Release --project src/Flare.Benchmarks -- export artifacts/bench/fixtures`:

```bash
npm run bench -- bench/component/decode.bench.ts                     # console table
npm run bench -- bench/component/decode.bench.ts --filter logs-page --time 200
npm run bench -- bench/workflow/client-stage.bench.ts --json out.json # JSON instead of a table
```

The MemoryPack path uses the generated decoders (ADR-0276) and the production mappers
(`toLogEventDto`, `toTraceDto`, `toMetricSeries`), which are exported from `src/lib/*-api.ts` for
this purpose. The JSON path decodes the bytes Flare.Api's System.Text.Json actually produces, and
maps them into the same UI types. `assertEquivalent` fails the run if the two paths ever disagree.
