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
  Also missing: local users can't change their own password at all
  (`AuthEndpoints` has only login/logout/bootstrap), and `ISessionStore` can't
  drop a user's sessions. Add `POST /api/auth/password` (current + new, same
  strength rules as bootstrap) that revokes the user's other sessions, and
  make any reset (forgot-password or admin) revoke all of them
  ([signoz#12531](https://github.com/SigNoz/signoz/commit/faaed20dbd08c320fcda4f9cc004d2091c4045de)).
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
- **Docs for the span duration percentile.** The "pN of `<name>` in
  `<service>`" line in `SpanDetailSheet` (`POST /api/spans/duration-percentile`,
  ±1h window, hidden under 10 similar spans) isn't mentioned in the traces
  how-to/reference pages yet. Document it there, with the `.ru`/`.fr`/`.zh-CN`
  siblings hand-translated. Not started.
- **Textbox dashboard variable.** `DashboardVariableSourceKind` is only
  `Query`/`Custom`, so there's no way to type a free value (a user ID, order
  ID, tenant) and have every panel filter on it. Add a `Textbox` kind with an
  optional default, where empty means "All"/don't touch the filter, same as
  other variables. It works with `$name` substitution and URL state. Not
  started. Prior art:
  [signoz#9843](https://github.com/SigNoz/signoz/commit/31e9e896ec84b2bfa48ae64dde5ba35896f8518c).
- **Provision the admin account from configuration.** The first admin can
  only be created interactively via `/api/auth/bootstrap`, so headless
  installs (compose, the `flare` CLI, Kubernetes/Helm) can't come up with a
  known login. Add optional `Identity__Admin__Username`/`__Password` (plus a
  `__PasswordFile` variant for secrets), applied on startup only when no
  admin exists. An explicit opt-in flag can also reconcile the password to
  config on every start, in which case that account is protected from
  deletion/demotion in the UI. Not started. Prior art:
  [signoz#10313](https://github.com/SigNoz/signoz/commit/6de4520a958fd68c733cf39dbb7594e6198e964d).
- **Custom legend format template for Metrics panels.** Series labels are
  automatic only (service + every attribute, compacted). Per-series color
  overrides exist, but there's no label pattern. Add an optional per-panel
  template such as `{{http.route}} {{http.response.status_code}}`, resolved
  per series (`{{service}}` for the service name, missing keys render empty)
  and falling back to the automatic label when unset. Keep color overrides
  keyed on the full series identity, not the rendered text. Not started.
  Prior art:
  [signoz#10529](https://github.com/SigNoz/signoz/commit/6fb92880cc4390838f372ccea2773b4e0e33b403).
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
- **LLM observability from GenAI semconv.** Nothing reads `gen_ai.*` span
  attributes today, though .NET apps using Microsoft.Extensions.AI or
  Semantic Kernel emit them. Add a page built from `gen_ai.*` spans: calls,
  latency, error rate, and input/output tokens by `gen_ai.request.model`,
  `gen_ai.system`/provider and service, with drill-down to traces. Add an
  editable model → price-per-token table for estimated cost (seeded with
  common models, overridable). Consider pre-aggregation like ADR-0031 if
  volumes warrant it. Needs an ADR. Not started. Prior art:
  [signoz#10908](https://github.com/SigNoz/signoz/commit/755390c4b5b2456a7c5c44d98fe8fcb18671616b).
- **Pin and tag dashboards.** Dashboards have no tags and no pinning: the only
  per-user ordering is the single home dashboard (`home-preference.ts`), which
  gets painful past a few dozen dashboards. Add free-form tags on dashboards
  (filter chips + search on the list page) and a per-user pin/favourite
  stored in Identity that floats pinned dashboards to the top of the list
  and the command palette. Not started. Prior art:
  [signoz#11219](https://github.com/SigNoz/signoz/commit/b22eef6a65211f66f5f0a50c6ce3b019fee4b532).
- **Metric attribute reduction: preview and docs.** The ingest engine,
  `/api/metric-attribute-rules` CRUD and the catalog's "Reduce attributes"
  section shipped (ADR-0083), but there's no dry-run preview of how many
  series a rule would remove, no page listing prefix rules that match no
  currently-ingested metric, and no user-facing how-to in `docs/`. Prior art:
  [signoz#11849](https://github.com/SigNoz/signoz/commit/d5221a6ff3b1b7a9b55492218c9f589845bcbc28).
- **"Dashboards using this metric" in the metrics catalog.** The catalog's
  inspect view doesn't show which dashboard panels (including formula
  panels) reference a metric, so before renaming, dropping or overriding a
  metric there's no way to see what breaks. Add a lookup over saved
  dashboards' layout JSON by metric name (respecting dashboard visibility),
  listed with deep links to each dashboard/panel. Not started. Prior art:
  [signoz#11784](https://github.com/SigNoz/signoz/commit/5ab6636863aa3cab0b5b0a7e260be8c3f206841c).
- **Free-text log search across all fields.** The free-text filter is a
  case-insensitive substring match on `Body` only, so an order ID that lives
  only in an attribute isn't found unless you know its key. Add an opt-in
  "search all fields" toggle that also matches attribute and resource values:
  `arrayExists(v -> positionCaseInsensitive(v, {q}) > 0,
  mapValues(LogAttributes))`, same for `ResourceAttributes`. Keep it opt-in
  because those branches can't use the body ngram index (ADR-0073), and
  mirror it in `LogFilterMatcher` for live tail. Not started. Prior art:
  [signoz#12244](https://github.com/SigNoz/signoz/commit/77c1b601be2a1baf49b2e69fcfdcb8d4119c84a6).
- **Microsoft Teams and Discord notification channels.** Channel types are
  Webhook/Telegram/Email/PagerDuty. The generic webhook's top-level `text`
  covers Slack and (probably; verify live) Google Chat incoming webhooks, but
  Teams Workflows webhooks need an Adaptive Card payload and Discord needs
  `content`, so neither works today. Add both as native types with send-test
  and template support (ADR-0052), and a docs line that Slack/Google Chat use
  the plain Webhook type. Lower-priority further targets once the per-type
  notifier shape exists: Jira / JSM Ops (create an issue/alert, resolve on
  recovery)
  ([signoz#12478](https://github.com/SigNoz/signoz/commit/160a1b018cd9cd7396ff8b6906be158ed16cfb0b))
  and incident.io
  ([signoz#12644](https://github.com/SigNoz/signoz/commit/e84a61d43f7f5a7b10a955f0e2b4444b7443d4e7)).
  Not started. Prior art:
  [signoz#12314](https://github.com/SigNoz/signoz/commit/e9726776ab7a1adfc50540925ae016f183bf7cbc).
- **JSON body fields as log table columns.** Log table columns are
  Time/Message plus pinned attributes. A JSON body path (e.g. `$.order.id`)
  can be filtered on but not shown as a column. Allow adding a body path as a
  column, using the same path syntax as `BodyJsonFilters`, extracted
  client-side from the already-loaded body (no extra query; empty when the
  body isn't JSON or lacks the path), and persisted like other column
  choices. Not started. Prior art:
  [signoz#12503](https://github.com/SigNoz/signoz/commit/a355996a5d1eddeec7416859daec4a570a56a126).
- **Text/Markdown dashboard panel.** `PanelType` is only
  `Logs`/`Traces`/`Metrics`, so a dashboard can't carry notes, runbook links
  or section headers (panel descriptions are plain text by design). Add a
  `Text` panel type holding Markdown, rendered through a sanitizer (no raw
  HTML, links only http(s)), with `$variable` substitution like panel titles.
  No query, so it's excluded from refresh/lazy-load. Not started. Prior art:
  [signoz#12712](https://github.com/SigNoz/signoz/commit/851abd2c93af8b9dba28224e36e0e3ffa0a01309).
- **Heatmap visualization for histogram metrics.** The `histogram`
  visualization shows one distribution for the whole range, not how it moves
  over time, the standard view for latency. The per-time-bucket bucket
  arrays already come back (`sumForEach` in `MetricSeriesQueryBuilder`, plus
  exponential histograms via ADR-0060), so add a `heatmap` visualization
  (x = time bucket, y = histogram bucket, color = count, log color scale
  option) with a hover readout. Mostly a renderer. Not started. Prior art:
  [signoz#12764](https://github.com/SigNoz/signoz/commit/fd032291f95ed6d7db248c946aa149b28ee8c116).
- **Percent (100%) stacking.** Only `stackedBar` exists, in absolute values,
  and time series can't stack. Add a stacking option (none/normal/percent) to
  `timeSeries` and `bar`, folding `stackedBar` into `bar` + normal with a
  migration of saved panels. Percent mode divides each bucket by its total
  and shows the y-axis as 0–100%. Not started. Prior art:
  [signoz#12632](https://github.com/SigNoz/signoz/commit/485aed0e1ae0928661f452df6ad058331cd5501a).
- **Built-in dashboard templates.** Flare ships no dashboards: users start
  empty or import Grafana JSON. Ship a few templates for what .NET apps emit
  by default (ASP.NET Core `http.server.*`, HttpClient `http.client.*`, .NET
  runtime GC/threadpool/exceptions, hostmetrics, Kubernetes), each using
  service/host variables. Install with one click as normal editable
  dashboards (not locked "system" ones), and validate them against the
  example shop's real metric names. Not started. Prior art:
  [signoz#12620](https://github.com/SigNoz/signoz/commit/7eb610287e81e4bd7f812507d6baba28efa20ec7).
- **"Create alert" from the Logs and Metrics explorers.** Dashboard panels
  can draft an alert from their query (`DashboardPanelCard`) and explorers
  can pin to a dashboard, but there's no "Create alert from this query" in
  the explorers themselves, so the filter has to be rebuilt by hand in the
  alert form. Add a toolbar action that opens `AlertRuleFormDialog` prefilled
  from the current explorer state (LogCount for Logs, metric threshold for
  Metrics), reusing the panel's drafting code. Not started. Prior art:
  [signoz#12981](https://github.com/SigNoz/signoz/commit/adfcebf855bf792c74acfd4b01f7a0fcf29a3831).
- **Exception → source code.** Exceptions show a stack trace but nothing
  links a frame to the code that ran. Use `code.filepath`/`code.lineno` (and
  the stack trace's own `in File:line` frames) plus the app's commit
  (`service.version` or a `vcs.revision`-style resource attribute, which
  SourceLink-enabled builds can stamp) to show the failing lines inline and
  link to the repo at that commit (GitHub/GitLab/Azure DevOps URL patterns,
  repo URL configured per service). Optional follow-up: an "explain this
  exception" LLM action with the real source in context, under the AI
  constraints below. Not started.
- **AI incident summary on alerts (opt-in).** When a rule fires, run the
  same data `flare export --trace-id` bundles (a representative failing
  trace, its logs, the rule's metric window) through an LLM. Add the summary
  to the notification and alert history: first error, failing span/service,
  what changed vs. the previous window. Constraints for every AI feature:
  off by default; bring-your-own model (OpenAI-compatible endpoint,
  including local Ollama); attribute/body redaction before anything leaves
  the box; record what was sent (feeds the audit-log item); bounded token
  budget per alert; never blocks or delays the plain notification. Needs an
  ADR. Not started.
- **Natural language → typed filters.** Let users type "5xx on checkout in
  the last hour, excluding health checks" and have an LLM produce a
  `LogFilter`/`SpanFilter` (incl. structural trace queries) as JSON, checked
  by the existing validators before it runs. That's safer than text-to-SQL,
  since the model can never issue arbitrary ClickHouse queries. Show the
  generated filter as normal editable chips so users learn the UI. Same AI
  constraints as the incident-summary item. Not started.
- **N+1 query detection.** The classic EF Core problem is detectable from
  spans Flare already stores: within one trace, the same normalized
  `db.query.text` (or `db.operation.name` + `db.collection.name`) repeated ≥ N
  times (default 10) under one parent span. Show it as a badge on the parent
  in the waterfall ("N+1: 48× SELECT … FROM Orders"), as a trace-list filter,
  and as a per-service "worst offenders" list over a time range (query-time
  `GROUP BY TraceId, ParentSpanId, statement` with caps; pre-aggregate only if
  needed). Not started.
- **.NET runtime health detectors.** Turn `System.Runtime` metrics into
  findings on the service page rather than charts to interpret. Thread-pool
  starvation: queue length rising while completed work items flatline. GC
  pause spikes / time-in-GC above a threshold. Lock contention rate jumps.
  Exception-rate jumps. Each finding links to the window and related
  traces. Pairs with the built-in dashboards item, which shows the raw
  metrics. Not started.
- **Deploy / version comparison view.** "Did my deploy break anything?" in
  one screen: pick a service and two `service.version` values (default: the
  latest vs the previous). Compare new Drain log patterns (ADR-0007),
  error-rate and p95 latency per endpoint, new exception types, and new
  outbound dependencies. Each row links into the explorers scoped to that
  version. Versions are detected from first-seen timestamps. No new storage
  is needed for v1. Not started.
- **SLOs with error budgets and burn-rate alerts.** Define SLOs on span data
  (availability: non-error ratio of a service/endpoint; latency: % of
  requests under a threshold) with a target and window (e.g. 99.5% over
  28d). Show remaining error budget, plus multi-window burn-rate alerting
  (e.g. 1h/5m fast burn, 6h/30m slow burn) through the existing alert
  pipeline and channels. Likely needs a pre-aggregated per-minute
  good/total table for long windows. Needs an ADR. Not started.
- **Continuous profiling (later).** Ingest the OTLP profiles signal once it
  stabilizes, store per-service profiles, and link spans to flame graphs of
  what the code was doing during that span. Placeholder for when the spec
  and .NET support settle. Not started.
