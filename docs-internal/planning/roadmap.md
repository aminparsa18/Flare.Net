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
- **Rate limiting.** Login brute-force protection shipped (see below) -
  two candidate spots left, not yet prioritized against each other:
  1. Personal access tokens on the query API (Flare.Api) — PATs let
     external scripts hit `/api/logs/search`/`/aggregate` with bearer auth;
     today's per-query execution caps (`max_execution_time`,
     `max_rows_to_read`, etc.) bound cost per request but nothing bounds
     request frequency per token.
  2. Alert notification flapping (AlertEvaluationWorker / notification
     channels) — a rapidly flapping threshold can spam Slack/Telegram/
     email/PagerDuty with duplicate fires; needs a cooldown/dedup or rate
     limit per rule. Likely the highest-value of the two since it's a
     concrete current gap rather than a hypothetical.
- **Custom, user-built dashboards** — shipped: Phase 1 (ADR-0023, CRUD +
  static viewer), Phase 2 (ADR-0024, drag/resize grid editor,
  add/rename/remove panels in place, session-only dashboard-wide
  time-range override), Phase 3 (session-only auto-refresh interval;
  whole-dashboard duplicate and JSON export/import round-trip; a
  per-browser "set as home page" preference that self-clears if the chosen
  dashboard is later deleted), and Phase 4: full-screen/TV mode (a
  session-only, per-browser nav-chrome toggle using the Fullscreen API); a
  "Create alert" action on a Logs/Metrics panel (deep-links into the
  Alerts page's create dialog, pre-filled with that panel's condition -
  Traces has no alert condition kind to draft into, so it's skipped there);
  and a dashboard-wide "Service" override (a session-only built-in
  variable - a fixed dropdown of known service names, not a saved/
  query-backed variable - see "Still open" below for the fuller version);
  and role-gated dashboard mutation (`RequireMember` on the create/update/
  delete endpoints, `AuthState.canMutate` hiding New/Rename/Duplicate/
  Delete/Edit/Add-panel/Pin-to-dashboard controls in the UI so a Viewer
  never hits a 403, per the SigNoz button-level precedent
  [signoz#1051](https://github.com/SigNoz/signoz/commit/5caf94f024c2447d04d7609c5e018ecd7cba1ed2)/
  [#1066](https://github.com/SigNoz/signoz/commit/6c5a48082b0ea6eec51accf57de29ab1e611222b));
  and importing this app's own dashboard-export JSON back in (an "Import"
  button next to Export, filling the one-way gap Phase 3 left open); and
  Phase 5 (ADR-0025): real dashboard variables - any number of named,
  user-defined ones (not Phase 4's single fixed built-in), each either
  `Query`-sourced (distinct values resolved live from Logs'/Traces' own
  attribute-value endpoints, or a wide-window service aggregate) or a fixed
  `Custom` list, backing either the `services` filter (Phase 4's own case,
  generalized) or an arbitrary attribute bag+key equality match on Logs
  and/or Traces panels (Metrics has no attribute filter to attach to) -
  see [`docs/how-to/build-custom-dashboards.md`](../../docs/how-to/build-custom-dashboards.md)
  for the user-facing walkthrough of all of the above; and variable
  chaining - one variable's choices narrowing based on another's selected
  value (ADR-0026, following prior art from
  [signoz#2036](https://github.com/SigNoz/signoz/commit/cd9768c73)/
  [#2037](https://github.com/SigNoz/signoz/commit/ca53136cb)) - via a
  `dependsOnVariableId` field resolved parent-before-child, no separate
  dependency graph kept around; and per-panel opt-out from a dashboard
  variable - `DashboardPanel.excludedVariableIds`, toggled per variable via
  a filter-icon popover in that panel's own header (edit mode, only shown
  once the dashboard has ≥1 variable) - so one panel can say "don't narrow
  me" while every other panel that variable applies to still is; and
  per-user dashboard ownership (ADR-0027) - mutation-gated, not
  visibility-gated: every dashboard stays visible to every authenticated
  user (same as saved views/alert rules), but update/delete now also
  require being the dashboard's own creator or an Admin, via a nullable
  `OwnerUserId` column where null (predates the column, or created while
  auth is disabled) means "anyone Member-and-up may still mutate it."
  More design notes worth baking in from the start: one global time range
  driving every panel, not per-panel pickers
  ([signoz#2013](https://github.com/SigNoz/signoz/commit/17f32e976)) -
  the viewer never rendered a per-panel `TimeRangePicker` (Traces panels
  have no chart at all; Logs/Metrics panels only showed their reused
  `VolumeChart`/`MetricChart`), but both of those charts' own built-in
  drag-to-zoom gesture could still silently re-fetch just that one panel
  into a custom range, diverging it from `timeRangeOverride` with no
  visual indicator - fixed by a new `allowZoom` prop (default `true`,
  the Explorer pages' own usage) that `DashboardLogsPanelBody`/
  `DashboardMetricsPanelBody` pass as `false`, disabling only the
  range-mutating branch (VolumeChart's harmless click-to-highlight-a-
  bucket path, which never touches the fetched range, still works); and
  exported dashboard JSON should carry only definitions, never embedded
  cached query results - SigNoz got this wrong first and fixed it later
  ([signoz#2052](https://github.com/SigNoz/signoz/commit/b72815ca2));
  Phase 3's export already follows this (LayoutJson never held cached
  results to begin with - see `Dashboard`'s own remarks in
  `DashboardModels.cs`), and Phase 5's variables followed the same rule
  (only definitions are persisted, never a resolved value or option list).
  Still open, not started:
  - Lazy-loading panels - only fetch/render what's in viewport, not
    every panel on page load
    ([signoz#2133](https://github.com/SigNoz/signoz/commit/af272a368)).
