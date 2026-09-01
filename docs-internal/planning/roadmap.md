# Roadmap

Forward-looking, still-open items only — no diary of what's already
shipped. See [`../README.md`](../README.md) for the rule this file exists
to enforce (a completed item is deleted here the same PR that ships it,
not checked off and kept); `git log` and the `adr`/`investigations`
folders are where "what happened and why" actually lives.

- **Retention policies + cold storage to S3-compatible object storage
  (RustFS).** A separate item from multi-node scaling (which shipped —
  see [`../adr/0003-distributed-tables-plain-names-and-sharding.md`](../adr/0003-distributed-tables-plain-names-and-sharding.md)
  and [`../../docs/explanation/clustering.md`](../../docs/explanation/clustering.md)):
  this one is retention/cold storage, not horizontal availability/
  throughput. Not started.
- **Kubernetes: a first-class persistent-storage API on `AddFlare`**
  (something like a `WithPersistentStorage(...)` chain method binding
  `{name}-clickhouse-data`/`{name}-redis-data`/`{name}-identity-data` to
  consumer-supplied `AddPersistentVolume` resources, beating "wire it up
  by hand"). Blocked on bumping the `Aspire.Hosting.Kubernetes` package
  past the `13.4.6` preview `Flare.Hosting.Aspire` currently pins —
  `AddPersistentVolume`/`WithPersistentVolume` don't exist in that
  version. Needs its own live e2e pass once picked up. Current manual
  workaround: [`../../docs/reference/aspire-hosting.md`](../../docs/reference/aspire-hosting.md#kubernetes).
- **Research: a real "skip-index effectiveness" signal for the Indexing
  page.** Deliberately not shipped after checking whether ClickHouse
  exposes this as reliable production telemetry — it doesn't, in a form
  the Indexing page can build on today (`system.query_log`'s counters
  combine primary-key and skip-index pruning; the per-index granule-drop
  line needs non-default logging most self-hosted deployments won't have
  on; `EXPLAIN indexes = 1`/`EXPLAIN ESTIMATE` only cover one ad-hoc query,
  not retrospective dashboard traffic). Open question for whoever picks
  this back up: is there a version- or config-gated ClickHouse mechanism
  that would make this honest rather than invented? If not, the deferred
  fallback is a differently-labeled, genuinely-computable proxy (e.g. "%
  of queries reading under N% of their table's total rows" from
  `system.query_log`) — real, just not skip-index-specific, since
  primary-key pruning contributes too.
- **CLI: a multi-signal `incident.zip` export mode.** A `--trace-id`-keyed
  `--include-trace`/`--include-logs`/`--include-metrics` mode for
  `flare export` that bundles trace + logs + metrics into one archive,
  instead of the current logs-only NDJSON/CSV stream. Deliberately held
  back: the shipped stdout/`-o` + shell composability (`> file`, `| jq`)
  already covers real usage; this is a real want, not an urgent one.
- **`Flare.Ingest`: MemoryPack for the internal Redis Streams buffer
  payload** (`ClickHouseFlushWorker`/`RedisStreamLogEventSink`/
  `MetricEventSink`/`SpanEventSink`/`RedisPatternClusterStore`'s
  `LogEvent`/`MetricEvent`/`SpanEvent`/`PatternClusterRecord` JSON
  serialization, ADR-0002). Not the HTTP API — a self-contained, fully
  Flare-controlled boundary that never crosses into the dashboard, sees far
  higher throughput than the Query API (every ingested event, not
  occasional UI clicks), and needs zero TypeScript changes. Flagged as the
  likely higher-ROI, lower-risk follow-on to `Flare.Api`'s Phase 0/Phase 1
  MemoryPack work (see
  [`../investigations/memorypack-serialization-migration-scope.md`](../investigations/memorypack-serialization-migration-scope.md)'s
  Finding 6) once that work's own open items (an ADR for the
  content-negotiation shape, broader live e2e coverage) are picked up. Not
  started.
