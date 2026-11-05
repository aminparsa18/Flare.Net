# Flare serialization benchmarks

JSON vs MemoryPack, measured where Flare actually uses MemoryPack:

| Boundary | ADR | Measured by |
|---|---|---|
| Ingest's Redis Streams buffer (.NET ↔ .NET) | [0017](../../docs-internal/adr/0017-memorypack-ingest-redis-buffer.md) | `redis-buffer` |
| Flare.Api request/response content negotiation | [0015](../../docs-internal/adr/0015-memorypack-content-negotiation-for-flare-api.md) | `api-codec`, `request-codec`, `server-stage` |
| Dashboard decoding with the generated TypeScript decoders | [0016](../../docs-internal/adr/0016-memorypack-dashboard-typescript-adoption.md), [0276](../../docs-internal/adr/0276-generated-memorypack-decoders-with-record-dictionaries.md) | `decode`, `request-encode`, `client-stage` |
| HTTP compression of API responses (gzip-1) | [0277](../../docs-internal/adr/0277-api-response-compression.md) | `compression`, `decompress`, `server-stage`, `client-stage` |

This project holds the .NET half (BenchmarkDotNet) and the fixtures; the TypeScript half lives in
[`src/dashboard/bench/`](../dashboard/bench/) (tinybench, run under Node/V8). One script drives both
and builds the report.

## Running

```bash
scripts/bench.sh                    # everything, full accuracy (~40 min; close other apps, stop Docker)
scripts/bench.sh --quick            # smoke run: BenchmarkDotNet short job, 150 ms per tinybench case
scripts/bench.sh component          # component suites only
scripts/bench.sh workflow           # end-to-end suites only
scripts/bench.sh api-codec decode   # any named suites
scripts/bench.sh --report <run dir> # rebuild a run's report.html from its raw/ data
```

Each run lands in `docs-internal/benchmarks/<yyyy-mm-dd>/` (`-2`, `-3`, ... for a second run that day;
`--out DIR` overrides). See [docs-internal/benchmarks/README.md](../../docs-internal/benchmarks/README.md)
for what a run folder holds and what to commit.

Steps the script runs:

1. `dotnet build -c Release`. This also regenerates the dashboard's MemoryPack TypeScript decoders,
   because Flare.Api's codegen runs on every build.
2. `dotnet run ... -- export artifacts/bench/fixtures` writes every scenario's response body exactly
   as Flare.Api sends it, as JSON and as MemoryPack, uncompressed and under each codec, plus
   `manifest.json` with the sizes. This is the only source of fixture bytes for the TypeScript side.
3. BenchmarkDotNet runs the selected .NET suites (`--artifacts artifacts/bench/bdn`, gitignored).
   The brief JSON and CSV reports are copied into the run's `raw/dotnet/`.
4. tinybench runs the selected TypeScript suites, writing JSON to `raw/node/`.
5. `scripts/bench-report/env.mjs` records the machine, OS, .NET/Node/V8 versions and git commit.
   `scripts/bench-report/build-report.mjs` then writes `results.json` and `report.html`, comparing
   against the previous run folder if there is one.

## Suites

**Component** suites measure one codec, one operation, on one payload:

| Suite | Where | What |
|---|---|---|
| `redis-buffer` | `Component/RedisBufferCodecBenchmarks` | `LogEvent` encode/decode, synthetic Typical and AttributeHeavy events |
| `api-codec` | `Component/ApiCodecBenchmarks` | encode and decode of each response scenario through STJ's source-generated context vs MemoryPack |
| `request-codec` | `Component/RequestCodecBenchmarks` | server-side decode of a `LogSearchRequest` body |
| `compression` | `Component/CompressionBenchmarks` | server compress time per scenario, format and codec (gzip-1, gzip-6, brotli-4) |
| `decode` | `dashboard/bench/component/decode.bench.ts` | bytes to parsed object (`decode`), and bytes to UI types (`decode+map`) |
| `decompress` | `dashboard/bench/component/decompress.bench.ts` | client decompress per codec (Node's native zlib) |
| `request-encode` | `dashboard/bench/component/request-encode.bench.ts` | dashboard-side encode of the same request body |

**Workflow** suites measure one half of a whole dashboard request:

| Suite | Where | What |
|---|---|---|
| `server-stage` | `Workflow/ServerStageBenchmarks` | query result object → production `ApiSerialization.Write` → `IResult.ExecuteAsync` into an in-memory response; compressed cases run inside the real `ResponseCompressionMiddleware` (gzip-1 is Flare.Api's registration, gzip-6 its `Level=Optimal`, brotli-4 the framework's Brotli provider for comparison) |
| `client-stage` | `dashboard/bench/workflow/client-stage.bench.ts` | received bytes → decompress → decode → the UI types `searchLogs`/`getTrace`/`queryMetric` return |

The report adds a modeled transfer time between the two halves (`bytes × 8 ÷ bandwidth` at 1 Gbps,
100 Mbps, 20 Mbps and 5 Mbps) and stacks the stages. Each stage is derived from these two suites:

| Stage | How it is derived |
|---|---|
| encode | `server-stage` with codec `none` |
| compress | `server-stage` minus the `none` case |
| decompress | `client-stage` minus the `none` case |
| decode + map | `client-stage` with codec `none` |

Codec levels are explicit numbers. On .NET 10, `CompressionLevel.Fastest` is gzip 1, and `Optimal`
is gzip 6 and Brotli 4, so these are the settings `UseResponseCompression` would choose (verified
on 2026-11-04). Brotli `Fastest` is quality 1.

## Scenarios and fixtures

`Fixtures/Scenarios.cs` is the registry. Every response suite, in both languages, iterates over it:

| Scenario | Source | Content |
|---|---|---|
| `logs-page.synthetic` | synthetic | 200 rows from `Fixtures/Synthetic/LogEventDtoFixtures` (modeled OTel attribute bags, seeded RNG) |
| `logs-page.captured` | captured | `POST /api/logs/search`, 200 rows |
| `trace.captured` | captured | `GET /api/traces/{id}`, the largest seeded trace (4 spans) |
| `trace-large.synthetic` | synthetic | 200 *captured* spans re-parented into one trace, because seeded traces are tiny |
| `metrics.captured` | captured | `POST /api/metrics/query`, `http.server.request.duration` p50–p99 by `url.path`, top 10, 30 s buckets |
| `logs-request.synthetic` | synthetic | a `LogSearchRequest` body, request suites only |

### Captured fixtures

`Fixtures/Captured/*.json` are real Flare.Api JSON responses, embedded into the assembly. The .NET
side deserializes them into the production types and re-encodes them in both formats, so both formats
always encode the same value. `capture-info.json` records when and how they were taken. To
re-record them:

```bash
docker compose -p flarebench up -d --build clickhouse redis ingest api
dotnet run --project examples/ExampleApp.Seeder -- overview structure cardinality funnel external messaging
scripts/bench.sh --capture http://localhost:8080   # FLARE_TOKEN=<PAT> if auth is on
docker compose -p flarebench down -v
```

Check the new files before committing them. They should hold seeded demo data only, with no real
hostnames, IPs, emails or secrets. A captured scenario whose file is missing is skipped, so the
project builds and runs without captures.

## Adding a benchmark

- **A new response scenario** (another endpoint, or a bigger page): add it to `Scenarios.Build()`,
  with a captured file in `Fixtures/Captured/` if it comes from the API. If it needs a new client
  decoder, also add its kind to `kindOf` in `dashboard/bench/support/fixtures.ts`, and the decoder
  and JSON mapper to `dashboard/bench/support/client.ts`. Every suite then picks it up, and the TS
  suites check that both formats produce identical UI data before timing.
- **A new .NET suite**: add a class under `Component/` or `Workflow/`, then add its name to
  `bdn_class` and the suite lists in `scripts/bench.sh`, and to `SUITE_OF_TYPE` in
  `scripts/bench-report/build-report.mjs`.
- **A new TypeScript suite**: add `dashboard/bench/{component,workflow}/<name>.bench.ts` that builds
  `Case`s and calls `runSuite('<name>', cases)`. Add it to the suite lists in `scripts/bench.sh`.
- **Show it in the report**: a suite's raw numbers always land in `results.json`; add a
  `componentChart(...)` block in `build-report.mjs` to chart them.

Any single suite can also be run directly:

```bash
dotnet run -c Release --project src/Flare.Benchmarks -- --filter '*ApiCodecBenchmarks*'
cd src/dashboard && npm run bench -- bench/component/decode.bench.ts   # needs artifacts/bench/fixtures from an export
```

## Tests

None. This project is measurement code with no product behaviour. The TypeScript suites assert that
the JSON and MemoryPack paths produce identical UI data before any timing, which is the correctness
check that matters here.
