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
  throughput. Not started. Prior-art design worth reusing, from SigNoz's
  TTL/cold-storage implementation ([signoz#1173](https://github.com/SigNoz/signoz/commit/5d080f5564c7839d0908db48bc8fff47d0e55648)):
  cold storage isn't app-level archival, it's ClickHouse's own tiered
  storage — an S3-backed disk/volume defined in ClickHouse's own config,
  with `ALTER TABLE ... MODIFY TTL ... DELETE, ... TO VOLUME 'x'` moving
  aged parts onto it, so a table's storage policy just needs assigning
  once (idempotent) rather than anything bespoke on Flare's side; a
  `GetDisks`-style read of `system.disks` lets the retention UI offer a
  dropdown of volumes actually configured instead of free text. Because
  that `MODIFY TTL` is a long-running ClickHouse mutation, the set-TTL
  API should be async and status-tracked (a small table keyed by a
  transaction id, `pending`/`success`/`failed`, one row per underlying
  table) rather than blocking the request — reject a second set-TTL call
  while one's still `pending` instead of queuing another mutation, and
  have the GET endpoint return both the *actual* TTL (parsed live from
  ClickHouse) and the *expected* one (what was last requested) plus
  status, so the UI can show "applying…" instead of a stale value.
- **Research: a real "skip-index effectiveness" signal for the Indexing
  page.** Deliberately not shipped — ClickHouse doesn't expose this as
  reliable production telemetry today. Full findings, including upstream
  ClickHouse's own attempt at exactly this (merged then reverted for a
  correctness bug) and what to check before revisiting:
  [`../investigations/skip-index-effectiveness-signal.md`](../investigations/skip-index-effectiveness-signal.md).
  Until upstream lands something reliable, the fallback is a
  differently-labeled, genuinely-computable proxy (e.g. "% of queries
  reading under N% of their table's total rows" from `system.query_log`) —
  real, just not skip-index-specific, since primary-key pruning contributes
  too.
- **LogQL attribute-map syntax.** The SQL query bar (`LogQlLexer`/
  `LogQlParser`/`LogQlAst`/`LogQlWhereTranslator` under
  [`src/Flare.Api/Query/LogQl/`](../../src/Flare.Api/Query/LogQl/)) only
  knows a fixed column list (`service`, `level`, `body`, `traceId`,
  `spanId`, `severityNumber`) - it has no syntax for reaching into the
  arbitrary key/value `LogAttributes`/`ResourceAttributes`/
  `ScopeAttributes` maps at all, unlike the structured `AttributeFilter`
  path (which now supports exists/absent/not-equals - see git history).
  A bigger change than that one: needs new grammar (e.g. `attributes.foo`),
  a new AST node, and translator support for `mapContains`/map-subscript
  SQL. Approach TBD (SQL-bar grammar vs. something else entirely) - not
  started.
- **Custom, user-built dashboards (multi-panel, saved, composed from
  arbitrary log/trace/metric queries).** A bigger item, likely needs its
  own design pass before implementation. Distinct from
  [`Saved views`](../../src/dashboard/src/routes/views) — a saved view is
  one named filter preset for a single Logs/Traces/Metrics page; a
  dashboard is a named collection of independent panels (mix of charts
  from different queries/signals) arranged on a grid. No CRUD for this
  shape exists anywhere in `Flare.Api` today. Not started. When this does
  get built: gate widget edit/delete and dashboard-description edit in
  the UI itself by role (admin/editor can mutate, viewer read-only), not
  just a 403 from the API — SigNoz does this at the button level
  (e.g. [signoz#1051](https://github.com/SigNoz/signoz/commit/5caf94f024c2447d04d7609c5e018ecd7cba1ed2),
  [#1066](https://github.com/SigNoz/signoz/commit/6c5a48082b0ea6eec51accf57de29ab1e611222b)).
  Also worth avoiding a pitfall SigNoz hit and walked back: don't wire
  grid-layout drag/resize directly to a persistence API call on every
  tick — they did, then removed it in favor of an explicit save
  ([signoz#1306](https://github.com/SigNoz/signoz/commit/63e663a92d88859f0ec5f5438ef9aba8641666ad)).
- **Resource-attribute filtering on the Traces › Services tab.** Today
  that tab is just the dependency graph + per-node External/Database
  breakdown (see [`../../docs-internal/investigations/`](../investigations/)
  service-dependency-map work) with no way to narrow it by resource
  attributes (e.g. `deployment.environment`, `host.name`). `LogFilter`
  already does key/value attribute filtering for logs; this would be the
  same shape applied to the Services view — filter chips backed by
  arbitrary resource-attribute key/value pairs, not just a service-name
  picker. Reference: [signoz#1022](https://github.com/SigNoz/signoz/commit/7948bca710c1ab1184515e03ef890168f765a7a7).
  Not started.
- **Searchable facet-value list in filter panels.** Logs/Traces filter
  sidebars render each facet (service name, attribute key, etc.) as a
  checkbox list with no way to narrow it by typing — fine for a handful
  of values, painful once a facet has dozens (many distinct service
  names, high-cardinality attribute values). SigNoz added a search box
  inside the facet's checkbox list itself. Reference:
  [signoz#1308](https://github.com/SigNoz/signoz/commit/224ec8d0d9d3ce0b9422c6c35dd378d3d5cd6449).
  Not started.
- **Cache-control headers on the dashboard's static assets.** Small ops
  tweak, not a product feature: self-hosted `docker-compose.yml`/nginx
  config for `src/dashboard` doesn't currently set explicit
  `Cache-Control` headers for built JS/CSS bundles, so repeat visits
  re-validate more than necessary. SigNoz added this at the nginx layer.
  References: [signoz#1104](https://github.com/SigNoz/signoz/commit/da386b0e8),
  [#1057](https://github.com/SigNoz/signoz/commit/a0643aaf4). Not started.
