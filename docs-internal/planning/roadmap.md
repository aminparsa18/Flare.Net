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
  Worth designing in from the start: per-resource retention (e.g. keep
  `deployment.environment=dev` for 7 days, everything else 30) via a
  `_retention_days` column computed from ordered resource-attribute rules
  (`multiIf(...)`) and a TTL of `Timestamp + toIntervalDay(_retention_days)`,
  with a default when no rule matches
  ([signoz#8513](https://github.com/SigNoz/signoz/commit/4daec45d987ab07a095f1c225db63193fef93f65)).
  Run every `MODIFY TTL` with `SETTINGS materialize_ttl_after_modify=0`,
  or each retention change rewrites every existing part up front instead
  of letting merges apply it
  ([signoz#9189](https://github.com/SigNoz/signoz/commit/7ddaa84387748a7ee36a1e19199078f86518eee3)).
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
  The same expiring set-password token should also back a forgot-password
  flow: there's no reset today, so a locked-out local user needs direct
  SQLite access. Email the link when SMTP is configured, otherwise let an
  admin generate one
  ([signoz#10073](https://github.com/SigNoz/signoz/commit/e1ac992e5a65b49678187303840e79b568feea87)).
- **OpenAPI.NET v3 (`Microsoft.OpenApi` 3.x, OpenAPI spec 3.2).** Blocked on
  `Microsoft.AspNetCore.OpenApi`: 10.0.x caps it at `[2.12.0, 3.0.0)`, so the
  direct pin in `Directory.Packages.props` stays on 2.x. The first release
  that allows 3.x is 11.0 (RC1 requires `[3.10.0, 4.0.0)`), which needs the
  `net11.0` upgrade, so do both together; no 10.0.x servicing release has lifted the cap.
  See the [OpenAPI.NET v2/v3 announcement](https://devblogs.microsoft.com/openapi/openapi-net-release-announcements/).
- **Dashboard edit mode hides narrow panels' titles.** On panels ≤ 4 grid
  columns wide at a ~1300 px viewport, the per-panel edit toolbar (move,
  alert, export, edit, filter, visualization, colors, row, duplicate,
  delete) takes the whole header: the title shrinks to nothing and the
  panel-type badge overlaps the icons. Needs an overflow menu or a
  wrapping header in edit mode.
- **Dashboard variable values and time range in the URL.** The dashboard
  viewer keeps variable selections and the time-range override in memory or
  localStorage only, so a shared `/dashboards/<id>` link opens with the
  recipient's own values. Mirror them into query params (e.g.
  `?var-service=a,b&range=1h`), hydrate from the URL first on load, and use
  `replaceState` on change. Not started. Prior art:
  [signoz#8874](https://github.com/SigNoz/signoz/commit/437d0d134502b5bd124471606674080a040a2090).
- **Docs for the span duration percentile.** The "pN of `<name>` in
  `<service>`" line in `SpanDetailSheet` (`POST /api/spans/duration-percentile`,
  ±1h window, hidden under 10 similar spans) isn't mentioned in the traces
  how-to/reference pages yet. Document it there, with the `.ru`/`.fr`/`.zh-CN`
  siblings hand-translated. Not started.
- **Host/pod metrics in span details.** Log event details already show
  `EventHostMetrics`/`EventPodMetrics` around the event's timestamp, but
  `SpanDetailSheet` doesn't. Reuse both components with the span's resource
  attributes (`host.name`, `k8s.pod.name`) and start time. Not started.
  Prior art:
  [signoz#8911](https://github.com/SigNoz/signoz/commit/155a44a25ddfaa4e71eb2c257a5256ae86c1b929).
- **"Same source" scope for the log context view.** `/api/logs/context` is
  deliberately global (see `LogContextRequest`'s remarks), so on a busy
  cluster the surrounding lines are mostly unrelated services. Keep global as
  one option and add a "same source" toggle in `LogContextSheet`. It scopes to
  the first resource attribute the anchor event has, in priority order
  `k8s.pod.name` → `container.id` → `host.name` → `service.name`, while
  staying time-unbounded. Not started. Prior art:
  [signoz#9303](https://github.com/SigNoz/signoz/commit/44d1d0f994e9446d4cfa5ee9a4ff8c4472aeb383).
- **Per-panel decimal precision.** Axis, tooltip, value and table
  formatting fix the precision (`maximumFractionDigits: 2` in
  `$lib/metrics/axis.ts`), so e.g. a ratio panel can't show 0.0042 usefully.
  Add an optional `decimals` setting (auto, or 0–6) to Metrics panels next to
  units, threaded through all visualizations. Not started. Prior art:
  [signoz#9054](https://github.com/SigNoz/signoz/commit/6db74a55858ae0b43c9842b931e84f4c62de799e).
- **JSON body tree view in log details.** `EventDetailSheet` renders the body
  as plain/ANSI text only, so a JSON body is one long string. When the body
  parses as JSON, offer a collapsible pretty-printed tree (keeping the raw
  view as a toggle and the 64 KB preview guard). Each node gets copy-value /
  copy-path and "filter by this path = value", which writes into the
  existing `BodyJsonFilters`. Not started. Prior art:
  [signoz#9657](https://github.com/SigNoz/signoz/commit/1078f983882760964dd6fa6e6784d2dee3dd3c2c).
- **Path/value suggestions for JSON body filters.** `BodyJsonFiltersRow` has
  no suggestion source (its fetcher always resolves empty), so users must
  know the exact JSON path. Add an endpoint that samples recent JSON bodies
  in the current time range/service filter (`JSONAllPaths`/`JSONExtractKeys`
  over a capped `LIMIT`, under the usual query caps), cached, plus a matching
  value lookup for a chosen path. That's preferable to an ingest-maintained
  key table. Not started. Prior art:
  [signoz#9593](https://github.com/SigNoz/signoz/commit/e66bfe59618917ed947135ba73bb00086e47a1e4).
- **Unit on metric-alert thresholds.** A metric rule's threshold is always
  in the series' native unit, so "alert when p95 > 500 ms" against
  `http.server.request.duration` (recorded in seconds) has to be typed as
  `0.5`. That's easy to get wrong silently. Add an optional threshold unit
  from the same unit family as the metric's (catalog unit or override,
  ADR-0065), converted to the native unit before evaluation, and shown with
  its unit in notifications and on the alert chart. Not started. Prior art:
  [signoz#10020](https://github.com/SigNoz/signoz/commit/8cabaafc584d1aa92a603d85b2c4d021dff9e911).
- **Textbox dashboard variable.** `DashboardVariableSourceKind` is only
  `Query`/`Custom`, so there's no way to type a free value (a user ID, order
  ID, tenant) and have every panel filter on it. Add a `Textbox` kind with an
  optional default, where empty means "All"/don't touch the filter, same as
  other variables. It works with `$name` substitution and URL state. Not
  started. Prior art:
  [signoz#9843](https://github.com/SigNoz/signoz/commit/31e9e896ec84b2bfa48ae64dde5ba35896f8518c).
- **"Replace filters with this" attribute action + logs for this trace.**
  `AttributeTable` actions only add an include/exclude filter, and a log's
  `traceId` only links to the trace view, so seeing every log line of the
  same trace means clearing filters by hand. Add a "replace filters with
  this" action (and a direct "Logs for this trace" link next to the trace
  ID) that resets the explorer to just that one filter, keeping the time
  range. Not started. Prior art:
  [signoz#10242](https://github.com/SigNoz/signoz/commit/0c660f86186328f9fc4ca280d3be05c3506058b4).
- **Provision the admin account from configuration.** The first admin can
  only be created interactively via `/api/auth/bootstrap`, so headless
  installs (compose, the `flare` CLI, Kubernetes/Helm) can't come up with a
  known login. Add optional `Identity__Admin__Username`/`__Password` (plus a
  `__PasswordFile` variant for secrets), applied on startup only when no
  admin exists. An explicit opt-in flag can also reconcile the password to
  config on every start, in which case that account is protected from
  deletion/demotion in the UI. Not started. Prior art:
  [signoz#10313](https://github.com/SigNoz/signoz/commit/6de4520a958fd68c733cf39dbb7594e6198e964d).
- **Previous/next navigation in the log details drawer.** `EventDetailSheet`
  only closes, so reading consecutive log lines means close → click →
  reopen. Add chevrons plus ↑/↓ (or j/k) keys that move
  `explorer.selectedEventId` to the adjacent row in the current result list,
  scrolling the table to keep it in view and loading the next page at the
  end. Not started. Prior art:
  [signoz#10250](https://github.com/SigNoz/signoz/commit/3aa0d8a7fd5616f46504b58c477adb726f599ce1).
- **Service accounts.** Personal access tokens (ADR-0019) are owned by a
  user, so CI, a Grafana datasource or a script has to borrow a human
  account and breaks when that person is disabled or leaves. Add a
  `ServiceAccount` principal in Identity (name, role, disabled flag) that can
  own PATs and go through the same auth handler and per-token rate limits
  (ADR-0028), plus admin-only management UI and an audit trail of who
  created/rotated which token. Needs an ADR. Not started. Prior art:
  [signoz#10436](https://github.com/SigNoz/signoz/commit/37cd1ab84b2c40aabcc2390b354fe87e64e91a7b).
- **Custom legend format template for Metrics panels.** Series labels are
  automatic only (service + every attribute, compacted). Per-series color
  overrides exist, but there's no label pattern. Add an optional per-panel
  template such as `{{http.route}} {{http.response.status_code}}`, resolved
  per series (`{{service}}` for the service name, missing keys render empty)
  and falling back to the automatic label when unset. Keep color overrides
  keyed on the full series identity, not the rendered text. Not started.
  Prior art:
  [signoz#10529](https://github.com/SigNoz/signoz/commit/6fb92880cc4390838f372ccea2773b4e0e33b403).
- **Large traces: surface truncation, then load lazily.** `TraceByIdQueryBuilder`
  caps a trace at `MaxSpans = 10_000`, and nothing says so. A 15k-span
  batch-job trace silently loses spans, and their orphaned children get
  hoisted to the root of the waterfall. First step (S): return a `Truncated`
  flag (query `MaxSpans + 1`) and show a banner on `/traces/[traceId]`.
  Second step (M): for traces above the cap, return the first N depth levels
  and fetch a span's subtree when it's expanded, keeping full fetch for small
  traces. Not started. Prior art:
  [signoz#10399](https://github.com/SigNoz/signoz/commit/58dabf9a6c45fb21eb5975af7cf85c11aa307d05).
- **Audit log.** There's no record of who changed an alert rule, notification
  channel, dashboard, pipeline rule, maintenance window, user role, PAT or
  auth setting, or when. Add an append-only `audit_events` table (actor,
  action, resource type/id, timestamp, a small before/after diff, source IP),
  written from the state-changing endpoints via endpoint metadata or a filter
  rather than ad hoc calls. Add an admin-only page with filters and a
  retention bound. The service-accounts item's "audit trail" would use it.
  Needs an ADR. Not started. Prior art:
  [signoz#10791](https://github.com/SigNoz/signoz/commit/42415e08739c4e8237e7856c0661f30638d68cd1).
- **Export traces from the dashboard.** Logs have `ExportDialog` and the CLI
  has `flare export --trace-id`, but the Traces explorer and trace detail page
  have no download. Add export of the current trace-list results (CSV/NDJSON,
  chosen columns, capped like log export) and a "Download trace JSON" button
  on `/traces/[traceId]`. Not started. Prior art:
  [signoz#9991](https://github.com/SigNoz/signoz/commit/c95523c747026f7b28562392ce81bc5cee4c5ca0).
- **Serve Flare under a sub-path behind a reverse proxy.** There's no
  base-path support: the dashboard sets no SvelteKit `paths.base` and the API
  has no `UsePathBase`, so `https://example.com/flare/` doesn't work and
  Flare needs its own (sub)domain. Add one setting (e.g. `Flare__BasePath`)
  that applies to API routes, auth/OIDC callback URLs, generated deep links
  in notifications, and the dashboard. `paths.base` is build-time in
  SvelteKit, so the published image needs a runtime approach (e.g. build
  with a placeholder base and rewrite it at container start). Not started.
  Prior art:
  [signoz#10943](https://github.com/SigNoz/signoz/commit/ef298af3885b4a0f4f38e49241e6316367721a9e).
- **Markdown in alert notification templates, rendered per channel.**
  Custom templates (ADR-0052) are sent as plain text. Telegram drops
  `parse_mode` for user text because it can't be guaranteed valid, and email
  has no HTML part. Parse templates as a small CommonMark subset (bold,
  italic, links, inline code, lists) and render each channel's format with
  proper escaping: Telegram HTML mode, Slack mrkdwn, an email HTML part
  alongside plain text, plain text for webhook/PagerDuty. Fall back to plain
  text if rendering fails, and show the per-channel output in the existing
  server-side preview. Probably a short follow-up ADR to 0052. Not started.
  Prior art:
  [signoz#10682](https://github.com/SigNoz/signoz/commit/30d3f754b56b39c4660ca18bdf8e4d8d0a38b845).
