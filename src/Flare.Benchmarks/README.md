# Flare.Benchmarks

Project-local BenchmarkDotNet numbers for the two MemoryPack migrations already shipped:

- [ADR-0017](../../docs-internal/adr/0017-memorypack-ingest-redis-buffer.md) —
  `Flare.Ingest`'s internal Redis Streams buffer payload
  (`RedisBufferSerializationBenchmarks`, benchmarking `Pipeline.RedisEventPayload`'s
  MemoryPack path vs the legacy `LogEventJsonContext` JSON path it replaced).
- [ADR-0015](../../docs-internal/adr/0015-memorypack-content-negotiation-for-flare-api.md) —
  `Flare.Api`'s HTTP request/response content negotiation
  (`ApiResponseSerializationBenchmarks`, benchmarking `Json.ApiSerialization`'s MemoryPack
  path vs its JSON default for `POST /api/logs/search`'s response shape).

These exist to get real `LogEvent`/`LogEventDto`-shaped numbers (representative
attribute-bag sizes/cardinality, see `TestData/*Fixtures.cs`) for a decision the codebase
already made, rather than leaning on the generic payload shapes the external
[SerializerBenchmark](https://github.com/aminparsa18/SerializerBenchmark) repo measures.
See `docs-internal/planning/roadmap.md`'s (now-removed) "Flare-specific
JSON-vs-MemoryPack benchmark" item and `docs-internal/investigations/` for the results
this project produced.

## Running

Not part of `dotnet build Flare.slnx`/`dotnet test`/CI — BenchmarkDotNet needs a Release
build and runs each benchmark in an isolated out-of-process copy, and a full run takes
several minutes. Run it manually:

```bash
dotnet run -c Release --project src/Flare.Benchmarks
# or a single class:
dotnet run -c Release --project src/Flare.Benchmarks -- --filter '*RedisBufferSerializationBenchmarks*'
```

Results (including a full Markdown/HTML/CSV report per class) land under
`BenchmarkDotNet.Artifacts/results/` relative to wherever `dotnet run` was invoked from
(repo root for the commands above), not necessarily this project's own directory.
