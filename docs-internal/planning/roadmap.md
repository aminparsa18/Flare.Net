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
  status, so the UI can show "applying…" instead of a stale value. One
  more ClickHouse config gotcha to get right when this is built: set
  `perform_ttl_move_on_insert: 0` on the S3 volume in ClickHouse's
  storage config - without it, ClickHouse evaluates the TTL-move rule
  synchronously on every insert once cold storage is configured, adding
  latency to the ingest path; the flag defers it to ClickHouse's
  background merge process instead
  ([signoz#1448](https://github.com/SigNoz/signoz/commit/f8f903848e914d529617c6e10c69b3644f8d4c30)).
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
- **Research: does the Logs free-text search actually use `idx_body`?**
  `LogFilterSqlBuilder` compiles `Search` to `Body ILIKE '%…%'`, but
  `idx_body` (`db/clickhouse/0001_logs.sql`) is a `tokenbf_v1` index, and
  ClickHouse's bloom-filter skip indexes aren't documented as usable for
  `ILIKE` - so every search may be full-scanning `Body` within the time
  window, and pattern (e) in
  [`../investigations/benchmark-ingest-and-query.md`](../investigations/benchmark-ingest-and-query.md)
  may be measuring a scan, not the index. First step: `EXPLAIN indexes = 1`
  on a real search against a live ClickHouse. If confirmed, likely fix is
  an additive `ngrambf_v1` index on `lower(Body)` with the search rewritten
  as `lower(Body) LIKE lower(…)` (new migration + probably an ADR). Prior
  art: [signoz#4787](https://github.com/SigNoz/signoz/commit/1585065fff9b7853d63e64abebf2887ecc42cc72).
- **Data-sources guides for more message brokers.** The Messaging page
  (ADR-0056) already picks up any broker whose .NET client emits OTel
  `messaging.*` spans, but the Data sources page only has Kafka and
  RabbitMQ guides. Candidates: MassTransit (built-in `MassTransit`
  activity source, `messaging.system` = the transport), and Azure Service
  Bus (Azure SDK activity sources, probably behind the SDK's experimental
  tracing switch; check). Verify each against a real broker before writing
  its guide (MassTransit over RabbitMQ, the Service Bus emulator image).
  Kafka's live run caught a receive+process double count that synthetic
  spans didn't. Not started.
- **Backlog for more brokers on the Messaging page.** Kafka (consumer lag)
  and RabbitMQ (queue depth, ADR-0057) fill the `Backlog` column. Next:
  Service Bus active/dead-letter counts via the collector's Azure Monitor
  receiver, Amazon SQS (`OpenTelemetry.Instrumentation.AWS` spans +
  CloudWatch depth), and NATS (NATS.Net v2 activity source). Each is one
  more metric lookup next to `MessagingQueryBuilder.BuildQueueDepth`. Not
  started.
- **Create/invite additional local users.** With local auth,
  `/api/auth/bootstrap` creates only the first admin, and
  `UserEndpoints` can list users, change a role and disable a user, but not
  create one - there's no API or dashboard path to a second local account
  (live e2e runs have had to write users into SQLite directly). Needed: an
  admin-only "invite user" (email/username + role → one-time
  set-password link, expiring) plus a dashboard form on the users page;
  bulk invite is a nice-to-have. Not started. Prior art:
  [signoz#6057](https://github.com/SigNoz/signoz/commit/fc4b55cb34b48fd3f47719be6ad6008b42d7e77d).
- **OpenAPI.NET v3 (`Microsoft.OpenApi` 3.x, OpenAPI spec 3.2).** Blocked on
  `Microsoft.AspNetCore.OpenApi`: 10.0.x caps it at `[2.12.0, 3.0.0)`, so the
  direct pin in `Directory.Packages.props` stays on 2.x. The first release
  that allows 3.x is 11.0 (RC1 requires `[3.10.0, 4.0.0)`), which needs the
  `net11.0` upgrade, so do both together; no 10.0.x servicing release has lifted the cap.
  See the [OpenAPI.NET v2/v3 announcement](https://devblogs.microsoft.com/openapi/openapi-net-release-announcements/).
- **Promote hot attribute keys to materialized columns.** Every attribute
  filter reads the whole `Attributes` map; an admin action on the
  Indexing page to "promote" a key (e.g. `http.route`) would add
  `ALTER TABLE ... ADD COLUMN attr_http_route String MATERIALIZED
  Attributes['http.route']` plus a skip index, recorded in a small
  registry the query builders consult to use the column instead of the
  map lookup. Needs an ADR (naming, backfill via `MATERIALIZE COLUMN`,
  cluster-mode DDL, demotion). Not started. Prior art:
  [signoz#6646](https://github.com/SigNoz/signoz/commit/67e822e23ef5744618b5a7d9e516c3c30a35e6c3).
- **Kubernetes infrastructure views from OTel k8s metrics.**
  `KubernetesResourcePoller` only lists Flare's *own* pods
  (`flare.resource=true`) and services - it's a view of Flare's stack,
  not of the user's cluster. Add list + drill-down pages for nodes, pods,
  deployments, statefulsets, daemonsets, jobs and volumes (CPU, memory,
  restarts, status) built from what the collector's `k8sclusterreceiver`
  / `kubeletstats` receivers ship over OTLP, following the Hosts page's
  pattern over the existing metrics tables (and reusing
  `/api/pods/metrics`). Large; phase it (nodes + pods first). Not
  started. Prior art: deployments/clusters/namespaces
  [signoz#6786](https://github.com/SigNoz/signoz/commit/403043e076bf60aa4b77a7df45eed06b286f5be0),
  statefulsets/daemonsets/jobs/volumes [signoz#6629](https://github.com/SigNoz/signoz/commit/813ca8bc230268d8904a786b18da8659045b18ce).
- **"Entry-point spans only" trace filter.** `SpanFilter.RootSpansOnly`
  gives each trace's single root span, but "the requests service X
  handled" needs each service's *entry* span - a span with no parent or
  whose parent belongs to a different service - since in a microservice
  chain most of those aren't the trace root. A filter flag (self-join or
  pre-computed at flush time, alongside the existing span rollups) plus
  a toggle next to the root-spans one. Not started. Prior art:
  [signoz#6910](https://github.com/SigNoz/signoz/commit/cc3d78cd716b1c926d6e0a07c8e7a0269a2ff560),
  per-query scope [signoz#6810](https://github.com/SigNoz/signoz/commit/044a124cc1b8735fcd4e3225e9fba2a1d357e533);
  also offer the same toggle on the service overview's top-operations
  table [signoz#8175](https://github.com/SigNoz/signoz/commit/41661a5e288fb396ea31a1b8040db0bbcb605357).
- **Show/hide the timestamp and body columns in the Logs table.** Next
  to the existing lines-per-row option: toggle the timestamp and body
  columns (e.g. hide body when only a few pinned attributes matter),
  carried in saved searches like the other display preferences.
  Frontend-only, small. Not started. Prior art:
  [signoz#6903](https://github.com/SigNoz/signoz/commit/98cdbcd711e8e32df8ad1283a87d1c90ca055546).
- **"Resolved" alert notifications.** Alerting is fire-only today:
  `AlertEvaluationWorker` checks a breach against the rule's cooldown and
  notifies, but when the condition recovers nothing is sent and there's
  no per-rule firing/ok state at all - on-call hears "it broke", never
  "it's fixed", and PagerDuty incidents stay open until closed by hand.
  Track firing/ok per rule (and per group for grouped rules), send a
  "Resolved" notification on the firing→ok transition, make it
  per-channel opt-out (`sendResolved`), and for PagerDuty send the
  Events API v2 `resolve` action with the same `dedup_key` so incidents
  auto-close; interaction with maintenance windows and absent-data rules
  to decide. Needs an ADR + an additive migration for the state. Not
  started. Prior art: per-channel `send_resolved`
  [signoz#7240](https://github.com/SigNoz/signoz/commit/8abba261a86692d5331e0cce283df572259193d8).
- **Metrics catalog with cardinality.** `/api/metrics/names` only feeds
  the Metrics picker (name, service, type, unit, description); there's
  no overview of what's being ingested. A catalog page: every metric with
  type, unit, description, active series count (cardinality), sample
  volume and last-received time, sortable to spot a cardinality
  explosion (e.g. a user id as an attribute) before ClickHouse feels it;
  a detail view with per-attribute distinct-value counts, and "related
  metrics" (same service / shared attributes). Queried from the existing
  metric tables with bounded `uniq`/`count` over a recent window. Not
  started. Prior art: summary [signoz#7200](https://github.com/SigNoz/signoz/commit/c2d038c025e9eaa5bd6773d2804ad9cfbffcd905),
  details [signoz#7238](https://github.com/SigNoz/signoz/commit/1b758a088c2f3ea224bacb2dcee5376d25bda0db),
  related metrics [signoz#7193](https://github.com/SigNoz/signoz/commit/735b56599233cd87af8216ae443cc93dc2b0e4e0).
  Worth folding in: an "inspect metric" view stepping through one
  series' raw samples and how time/space aggregation reduces them
  [signoz#7197](https://github.com/SigNoz/signoz/commit/9df23bc1ed85c0933a389726fbc74d6ac97c7ae7),
  and admin overrides for a metric's unit/description/type
  [signoz#7235](https://github.com/SigNoz/signoz/commit/5b6b5bf359a5940c21681639e7f1094a2fa3a5d9).
- **External API monitoring by domain.** `ServiceCallBreakdownQueryBuilder`
  groups external calls by `peer.service` only, but .NET's `HttpClient`
  instrumentation doesn't set it - it sets `server.address`, `url.full`,
  `http.request.method` - so a typical .NET app's calls to Stripe/Twilio/
  another team's API likely don't appear in the per-service call
  breakdown or as Service Map external nodes at all. Step 1 (small,
  arguably a bug fix): fall back to `server.address` when `peer.service`
  is empty, in the breakdown and the Service Map rollups. Step 2
  (medium): a dedicated page listing every external domain with
  per-endpoint (method + templated path) request rate, latency
  percentiles and error rate, drilling into matching traces. Not
  started. For the page's shape, SigNoz's later iterations settled on:
  sortable domain and endpoint tables (with port and last-seen columns),
  per-endpoint error rate, a status-code breakdown, a "top errors" tab
  per domain, a "dependent services" table (which of *our* services call
  this domain), and trace drill-down scoped to a window around the
  selected point. Prior art: [signoz#7308](https://github.com/SigNoz/signoz/commit/02f3dfefb90b75ccee7ef07b14f903c1dfce5359),
  [signoz#7432](https://github.com/SigNoz/signoz/commit/0b7cd4c1a74b8cee2c844f1b6c1374c1f84be447),
  top errors per domain [signoz b86e65d](https://github.com/SigNoz/signoz/commit/b86e65d2ca78a1f1a4e39680aaf47faa9055a547).
- **Trace funnels.** Define an ordered set of steps (span A → span B →
  span C, each a service + span-name/attribute match, e.g. checkout →
  payment → confirmation) and measure across traces in a window: how
  many traces reach each step, drop-off, step-to-step latency and error
  rate, with drill-down into traces that dropped at a given step. Large;
  needs an ADR (step matching, same-trace ordering semantics, query cost
  at scale). SigNoz's first attempt was reverted, so spike first. Not
  started. Prior art: [signoz#7315](https://github.com/SigNoz/signoz/commit/3100d602c43f12b2b7b5f029d2c8ea29531adda7),
  list page [signoz#7324](https://github.com/SigNoz/signoz/commit/2c87d96d753e9a786234e707783413f9de25e672).
- **Open a dashboard panel in its explorer.** `DashboardPanelCard` only
  deep-links to alert creation; add "Open in Logs/Traces/Metrics" (the
  panel's query + the dashboard's effective time range and variable
  values) and click-a-chart-point → the explorer narrowed to a window
  around that point (and that series' group, if grouped). Frontend-only
  - the explorers already restore from `?state=`. Not started. Prior
  art: [signoz#7141](https://github.com/SigNoz/signoz/commit/0320285a251eaa89a45f5336620c5bfd8143b6bb).
- **Logarithmic y-axis for charts and panels.** Only
  `ValueDistributionChart` offers a log scale; Metrics charts and
  dashboard panels can't, so a 10ms and a 10s series can't share a
  readable chart. A per-chart/per-panel linear/log toggle (carried in
  saved views and panel JSON), handling zero/negative values. Frontend-
  only, small. Not started. Prior art:
  [signoz#7413](https://github.com/SigNoz/signoz/commit/bc17a10550228152e2f8456a0e18ef0c1d084e79).
- **Resource-attribute filter on the Errors page.** `ExceptionFilter` only
  has time range + services - no "exceptions in production only" or
  "only on `service.version` 2.3". Reuse the `ResourceAttributeFilter`
  shape Logs/Traces already use in `ExceptionFilterSqlBuilder` and add
  the control to the /errors toolbar (plus a facet-sidebar entry). Not
  started. Prior art:
  [signoz#7589](https://github.com/SigNoz/signoz/commit/f11b9644cf4818f8ddbc87442202eb7e9b6081f9).
- **Deep link to a specific span.** `/traces/[traceId]` has no span
  parameter, so a shared link lands at the top of the waterfall. Accept
  `?span=<spanId>` (select it, expand its ancestors, scroll into view)
  and add "Copy link to span" in the span detail sheet. Frontend-only,
  small. Not started. Prior art:
  [signoz 0944af3](https://github.com/SigNoz/signoz/commit/0944af3d31e38481b7ae549a3c253f3c4090554c).
- **Sort traces by duration or span count.** The trace list is always
  newest-first (`SpanSearchQueryBuilder`'s `ORDER BY StartTime DESC,
  TraceId DESC, SpanId DESC`, keyset-paged on `StartTime`); "slowest
  requests in the last hour" is only approximable with `MinDurationNano`
  plus scrolling. Add sortable columns (duration, span count) to
  `TraceList`, with a matching keyset cursor per sort key (e.g.
  `(Duration, SpanId)`), span count coming from the trace rollups. Not
  started. Prior art:
  [signoz#7842](https://github.com/SigNoz/signoz/commit/503e4cdf00c9c767228a075a43b2a09298e17334).
- **Bug: metric points flagged "no recorded value" are ingested as real
  values.** OTLP data points carry `DataPointFlags.FLAG_NO_RECORDED_VALUE`
  (bit 1) meaning "no value - this series went stale"; `OtlpMetricsMapper`
  never reads `Flags`, so such points land with their empty value
  (usually `0`). Common behind an OTel Collector's Prometheus receiver,
  which emits them as staleness markers whenever a scrape target goes
  away (pod replaced, rollout): gauges dip to 0 on every deploy,
  averages get dragged down, and Gauge Last/Min alerts (ADR-0049) can
  false-fire. Fix: skip points with `(Flags & 1) != 0` in the number,
  histogram and exponential-histogram paths, plus mapper unit tests.
  Small. Not started. Prior art:
  [signoz#7674](https://github.com/SigNoz/signoz/commit/74bbb260331b9ca53107f1b85ad656a10a932db1).
- **Dashboard variables in panel titles + variable descriptions.** Panel
  titles are plain text; allow `$variable` references (e.g. "Latency –
  $service") resolved against the current selection (multi-value joined,
  "All" rendered as such), with `$`-triggered suggestions in the title
  field; and an optional per-variable description shown as a tooltip on
  the variable picker. Frontend-only, small. Not started. Prior art:
  titles [signoz#7898](https://github.com/SigNoz/signoz/commit/f10f7a806f102d53d4f66e88542afd98707c1f2d),
  descriptions [signoz#7897](https://github.com/SigNoz/signoz/commit/9383b6576d3e287b6aff8ae66980ea3ae12a0ed5).
- **Panel legend placement + per-series colors.** Series colors come
  from the fixed 5-slot palette hashed by series identity
  (`lib/metrics/chart-colors`), with no per-panel override and the legend
  always below the chart. Add a legend position option (bottom/right/
  hidden) and optional per-series color overrides (keyed by series
  label), stored in the panel JSON. Frontend-only, small. Not started.
  Prior art: legend options [signoz#8035](https://github.com/SigNoz/signoz/commit/aaeffae1bd53a2f5f8b805dd7aef993b5cef10f2),
  custom colors [signoz#8063](https://github.com/SigNoz/signoz/commit/8990fb7a7300dff265d3c3a83bb042388951c11e).
- **Span event markers on the waterfall.** Span events (incl. exceptions)
  are only listed in `SpanDetailSheet`; the waterfall doesn't show where
  in a span an event happened, or which spans had one at all. Draw a dot
  per event at its timestamp on the span bar (exceptions in the error
  color) with a hover preview (name, time offset, key attributes).
  Frontend-only, small. Not started. Prior art:
  [signoz#7889](https://github.com/SigNoz/signoz/commit/3fc6f7ee63d7a6c6e6c47e25d72964241b203e7a).
- **Flame graph view for traces.** Only the waterfall exists; a flame
  graph (span width = duration, stacked by depth, colored by service)
  shows where time goes in a large trace at a glance. A toggle on the
  trace detail page over the already-loaded spans, sharing selection
  with the span detail sheet. Frontend-only, medium. Not started. Prior
  art: [signoz#7889](https://github.com/SigNoz/signoz/commit/3fc6f7ee63d7a6c6e6c47e25d72964241b203e7a).
- **Structural trace queries (trace operators).** `SpanFilter` matches
  single spans only; add relationship operators across span conditions -
  `A => B` (A has descendant B), `A -> B` (direct child), plus
  AND/OR/NOT - e.g. "traces where `checkout` calls `payment` and
  `payment` errored", returning matching traces. Large; needs an ADR
  (ClickHouse evaluation strategy - per-trace `groupArray` + parent-id
  walk vs. a self-join bounded by `TraceId` - cost at scale, query
  syntax). Shares machinery with the trace funnels item. Not started.
  Prior art: [signoz#8165](https://github.com/SigNoz/signoz/commit/eeb2ab3212f20a7b6e8edda0a8a60c074469d0e6).
- **"New version available" notice.** Self-hosted users get no signal
  that they're behind. `Flare.Api` checks the latest GitHub release
  (cached, e.g. daily; opt-out config for air-gapped installs), and the
  dashboard shows a dismissible notice with the release notes when the
  running version is older. Small. Not started. Prior art:
  [signoz#8270](https://github.com/SigNoz/signoz/commit/3b1bf34d3e8faf850eb551d62914c85569d3a468).
- **`ParseJson` (flatten) pipeline-rule action.** Many apps log a JSON
  string as the body (Serilog JSON formatter, Console JSON, Node/Python
  loggers); Flare can *query* it (body-JSON filters) but pipeline rules
  only have `ExtractRegex`/`RedactRegex`, so those fields never become
  real attributes (no facets, group-by, pinned attributes or attribute
  actions on them). Add a `ParseJson` action that parses the body and
  flattens nested keys into attributes (`{"user":{"id":7}}` →
  `user.id=7`), with optional key prefix, max depth and max key count
  so a huge body can't blow up the attribute map - in both the ingest
  executor and the Api preview mirror (ADR-0033/0034). Medium. Not
  started. Prior art: [signoz#8227](https://github.com/SigNoz/signoz/commit/d6eed8e79dae5281839b461f46c1fbffe44b8bca),
  UI [signoz#8331](https://github.com/SigNoz/signoz/commit/ddb08b388362c339e3d152d43ead6f1df235f35a).
- **Dashboard UX batch: system theme, real links, huge-body guard.**
  (a) The user menu offers only Light/Dark (`NavUserMenu.svelte`); add
  "System" (mode-watcher already supports it). (b) Clickable rows
  navigate via `onclick={() => goto(...)}` (e.g. `TraceRow.svelte`), so
  Ctrl/Cmd+click, middle-click and "Open in new tab" don't work - make
  them real `<a href>` links (traces, alerts, dashboards list, exception
  groups). (c) `EventDetailSheet` renders the full body through
  `AnsiText` with no cap, so a multi-MB body can freeze the tab - render
  the first ~64 KB with "Show full body"/"Copy". Frontend-only, small.
  Not started. Prior art: system theme [signoz#8567](https://github.com/SigNoz/signoz/commit/a57698249738b7773e18b3ba7b9a2602526a3d44),
  new-tab clicks [signoz#8607](https://github.com/SigNoz/signoz/commit/d7fdbcd90dafde4e783ee6e5c0eb8959d5efb6c3),
  large-body safeguard [signoz#8560](https://github.com/SigNoz/signoz/commit/b40fda02cfb7b7d57724a3cf3044790e2995b841).
- **Attribute actions in the span detail view.** The log event detail
  view got filter-for/filter-out/copy (+ pinning) on `AttributeTable`
  (PR #309), but `SpanDetailSheet` renders the same component with only
  `title`/`attributes`, so span and resource attributes are display-only
  - `http.route=/checkout` on a slow span can't jump to "all traces with
  that route". Wire the same actions into the Traces explorer's filter
  state (`SpanFilter` attribute filters, span vs. resource bag), and
  optionally add count badges on the span detail tabs. Frontend-only,
  small. Not started. Prior art: span actionables
  [signoz#8761](https://github.com/SigNoz/signoz/commit/fdcad997f58145da462a57672c4a1d74f15baad5),
  tab count badges [signoz#8702](https://github.com/SigNoz/signoz/commit/5412e7f70b1b859928dca053b3f6a0552e941d17).
