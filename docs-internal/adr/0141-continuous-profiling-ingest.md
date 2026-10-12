# ADR-0141: Continuous profiling ingest (OTLP profiles, Alpha)

Status: Accepted

Date: 2026-10-06

## Context

OpenTelemetry's fourth signal, profiles, reached public Alpha in March 2026 (Collector
v0.148.0 added a pprof receiver and Kubernetes enrichment). The proto lives in
`opentelemetry/proto/profiles/v1development` and is still allowed to change. The roadmap
item waited for the spec to settle; Alpha plus Collector support is enough to start storing
the data, as long as the pieces that depend on the unstable wire shape stay small.

OTLP profiles carries one request-wide `ProfilesDictionary` (strings, functions, locations,
stacks, links, attributes) that every sample indexes into.

## Decision

**Accept the profiles signal on the existing OTLP ports and store one ClickHouse row per
`Sample`, with the dictionary resolved at ingest.**

- **Receiver.** gRPC `ProfilesService/Export` on :4317 and `POST /v1development/profiles` on
  :4318 (protobuf and JSON), through the same ingest-key auth, size cap and per-key limits as
  the other signals. The protos are vendored at the `v1.11.0` tag already pinned for the
  other three signals.
- **Pipeline.** A fourth parallel pipeline (`flare:profiles` Redis Stream, `ProfileFlushWorker`),
  not a generalisation of the span one, same call as ADR-0017's workers. At-least-once via the
  consumer group.
- **Storage.** `profile_samples` (migration 0065, plus the cluster variant sharded by
  `ServiceName`). The stack is `Array(LowCardinality(String))` of frame names **root first**,
  inlined functions expanded into their own frames, plus a `MATERIALIZED cityHash64(Stack)`
  column in the sort key. A flame graph is `GROUP BY Stack` with `sum(Value)`; no dictionary
  join. Unsymbolized native frames keep `module+0xaddr` so they stay distinct.
- **Span correlation.** `Sample.link_index` is resolved to `TraceId`/`SpanId` columns, bloom
  filtered on `TraceId`. That is the join key for "flame graph of this span".
- **Value.** The sum of `Sample.values`, or the timestamp count for timestamps-only samples
  (each counts as 1), in `SampleUnit`. `Sample.timestamps_unix_nano` is otherwise not kept:
  per-observation timing inside a profile has no consumer yet.
- **Tolerance.** Every dictionary index is bounds-checked and resolves to the zero value, so a
  buggy producer degrades one sample rather than failing the export.

- **Query API.** `POST /api/profiles/types` lists the (service, sample type) series in a window;
  `POST /api/profiles/flamegraph` returns the merged call tree for one service and sample type,
  optionally narrowed by `traceId`/`spanId`. The tree is built in C# (`FlameGraphBuilder`) from a
  single `GROUP BY Stack` capped at 5,000 stacks (`truncated` says when the cap hit), with
  `Total` and `Self` per frame. Project scoping applies through `ServiceScope`, like every other
  query over `ServiceName`.

## Consequences

- Rows repeat each frame name, trading some storage for no query-time join. `LowCardinality`
  and ZSTD keep that cheap; a stack-dictionary table is the follow-up if it isn't enough.
- `v1development` field changes mean a re-vendor must re-run the mapper tests
  ([Protos/VENDORED.md](../../src/Flare.Ingest/Protos/VENDORED.md)).
- The Ingestion and Pipeline pages in `Flare.Api` mirror `IngestionSignal` and don't list
  profiles yet; the stats are recorded under `profiles` and will show once the API enum follows.
- Not done here: flame-graph page, span-to-profile link in the waterfall,
  retention, and a .NET profiler source (the Collector's pprof receiver or eBPF agent are the
  practical senders today).
