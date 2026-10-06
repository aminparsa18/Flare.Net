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
- **Verify the Service Bus and SQS Messaging backlog against real clouds.**
  ADR-0119 and ADR-0120 took metric and attribute names from the Collector
  receivers' source, not a live run. Confirm `azure_activemessages_average` with
  `metadata_entityname` (`azuremonitor` receiver) and
  `ApproximateNumberOfMessagesVisible_avg` with `QueueName` (`awsfirehose` +
  `transform`), and fix the constants in `MessagingQueryBuilder` if they differ.
- **OpenAPI.NET v3 (`Microsoft.OpenApi` 3.x, OpenAPI spec 3.2).** Blocked on
  `Microsoft.AspNetCore.OpenApi`: 10.0.x caps it at `[2.12.0, 3.0.0)`, so the
  direct pin in `Directory.Packages.props` stays on 2.x. The first release
  that allows 3.x is 11.0 (RC1 requires `[3.10.0, 4.0.0)`), which needs the
  `net11.0` upgrade, so do both together; no 10.0.x servicing release has lifted the cap.
  See the [OpenAPI.NET v2/v3 announcement](https://devblogs.microsoft.com/openapi/openapi-net-release-announcements/).
- **Continuous profiling (later).** Ingest the OTLP profiles signal once it
  stabilizes, store per-service profiles, and link spans to flame graphs of
  what the code was doing during that span. Placeholder for when the spec
  and .NET support settle. Not started.
- **Scheduled dashboard reports.** There's no way to email a dashboard on a
  schedule (weekly SLO/latency report to a team). Add per-dashboard
  schedules (cron, recipients, relative time range, variable values), a
  runner in `Flare.AlertWorker` that renders the dashboard to PDF/PNG via
  headless Chromium (Playwright) using a short-lived service token, sent
  through the existing SMTP config, and run history with errors. Needs an
  ADR (rendering dependency, auth). Not started. Prior art:
  [signoz PR #10809](https://github.com/SigNoz/signoz/pull/10809) /
  [#10810](https://github.com/SigNoz/signoz/pull/10810) (open, unmerged).
- **User settings page (`/settings`), starting with Appearance & layout.**
  The dashboard has outgrown the user-menu dropdown as the home for
  preferences: theme, language and display time zone live in
  `NavUserMenu.svelte`, and the rest are scattered across per-feature
  `localStorage` keys with no UI to see or reset them (pinned log
  attributes, facet sidebar prefs, logs column visibility and lines per
  row, home-dashboard choice, last-used saved views, recent searches and
  custom ranges, update-notice dismissal). Build a sectioned settings
  route (left rail of sections, deep-linkable `/settings/<section>`) and
  move the dropdown's controls there, leaving the dropdown as a shortcut.
  Not started. Remaining: instance defaults an admin can set for new users
  (theme, layout, time zone).
- **Richer escalation and ack integrations.** Escalation (ADR-0125), the
  `flare alerts ack|snooze|unack` commands, on-call rotations (ADR-0126) and the signed
  acknowledge link in notifications (ADR-0127), a second escalation step (ADR-0136) and
  rotation overrides (ADR-0137) shipped.
  Still missing: a Slack button and PagerDuty ack sync (inbound integrations that need
  per-instance setup), more than two steps, and time-of-day restrictions on rotations.
  Needs an ADR.
- **Log-based metrics.** Turn a saved `LogFilter` (plus optional group-by) into
  a persisted metric via a ClickHouse materialized view, so charting or
  alerting on "count of X" doesn't scan logs each time. Natural home is a new
  pipeline-rule-style definition (ADR-0033); watch cardinality of the group-by
  and surface it on the cardinality page. Needs an ADR.
- **Config-as-code beyond alerts.** `flare alerts export`/`import` and
  dashboard JSON export exist, but SLOs, notification channels, pipeline rules,
  maintenance windows, metric attribute rules and ingest keys can't be moved or
  versioned. Generalize to a single `flare apply -f` / `flare export --all`
  with the same by-name references and never-export-credentials rules as the
  alert export. The Terraform provider below builds on this. Not started.
- **Terraform / OpenTofu provider.** Manage Flare declaratively next to the
  infrastructure it observes. The API is already close to provider-shaped:
  alerts, SLOs and notification channels have full CRUD by GUID id, and service
  accounts with access tokens (ADR-0082) give a non-interactive credential, so
  the provider authenticates with a token and needs no session flow. Ship as a
  separate repo (Go, `terraform-plugin-framework`) published to the Terraform
  and OpenTofu registries, v1 resources: `flare_notification_channel`,
  `flare_alert_rule`, `flare_slo`, `flare_dashboard`, `flare_pipeline_rule`,
  `flare_maintenance_window`, `flare_ingest_key`, `flare_service_account`.
  Open questions to settle in the ADR: (1) the OpenAPI document is only mapped
  in Development (`MapOpenApi` in `Flare.Api/Program.cs`), so decide whether to
  publish it as a build artifact and generate resource schemas from it
  (`terraform-plugin-codegen-openapi`) or hand-write them; (2) references are
  by id in the API but by name in the alert export, so the provider should
  expose names as the stable key and resolve ids itself, otherwise plans churn
  on recreated channels; (3) credentials (webhook URLs, SMTP, ingest-key
  secrets) are write-only and never returned, so they need `sensitive`,
  write-only attributes and a drift story; (4) API version compatibility,
  since the provider and the server release separately (use `/api/version`,
  ADR-0068). Depends on the config-as-code item for the by-name conventions
  and for any resource that has no CRUD endpoint yet (maintenance windows,
  metric attribute rules). Also ship `flare_*` data sources for channels and
  services so alert rules can reference them. Not started.
- **Official Helm chart for Flare.** The install paths are Aspire, compose and
  the CLI; Kubernetes users have only the Aspire publish output. Ship a chart
  for ingest, api, dashboard, alert worker, Redis and ClickHouse (single node,
  with cluster mode via the ClickHouse operator or external ClickHouse), with
  values for Identity Postgres (ADR-0111), ingress and sub-path hosting
  (ADR-0078). Document it under `docs/how-to/`.
- **OTLP forwarding and archive export.** No way to copy ingested telemetry
  elsewhere. Add per-ingest-key or per-service forwarding of logs, traces and
  metrics to another OTLP endpoint (migration and dual-write) and an optional
  Parquet/NDJSON archive to S3-compatible storage; coordinate with the cold
  storage item so the two don't both claim the S3 config surface.
- **Browser RUM and source maps (later).** No browser signal exists. Start with
  accepting OTel-JS / Faro browser telemetry through the existing OTLP path
  (page loads, web vitals, JS errors), a source-map upload API for symbolicating
  stack traces on `/errors`, and a Frontend page. Only worth it if Flare targets
  full-stack teams, not just .NET backends.
- **Usage and cost view (later).** Roll up per-service and per-ingest-key
  volume (events/day, bytes on disk, largest attributes) from data the
  Ingestion, Indexing and cardinality pages already read, so users can see
  what's driving storage before choosing sampling or retention rules.
- **Status page (later).** A public read-only page of service health and SLO
  status. Depends on synthetic monitoring and SLOs (ADR-0108) shipping first.
- **Shared alert notification templates.** ADR-0052 templates are per rule
  (`NotificationTitleTemplate` / `NotificationBodyTemplate` on `AlertRule`,
  edited in the rule form), so the same wording on many rules is pasted and
  maintained once per rule. Add named, reusable templates managed on a
  Settings page next to notification channels: a rule picks a template by
  reference (its own inline text stays as an override), editing a template
  updates every rule that uses it, and an instance-wide default template
  applies to rules that pick none (the built-in wording in
  `AlertMessageFormatter` stays the fallback, so existing rules are
  unchanged). Same `{{placeholder}}` set and save-time validation as
  ADR-0052 (`AlertTemplateRenderer`), same live preview. Design points for
  the ADR: separate fired / resolved bodies (today `{{status}}` is the only
  difference); an optional per-channel-type body (short for Telegram, long for
  email, since the title already maps onto each channel's own subject field);
  deleting a template that rules still use should be refused or list the rules;
  `flare alerts export`/`import` and the Terraform provider reference templates
  by name; audit-log entries (ADR-0079). Needs a ClickHouse or Identity-store
  migration decision (config tables follow ADR-0009 / ADR-0074 conventions) and
  an ADR that supersedes or extends ADR-0052. Not started.
