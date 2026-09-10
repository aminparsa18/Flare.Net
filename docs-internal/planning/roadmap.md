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
- **Attribute filter flags for `flare search`/`flare traces` (Flare.Cli)
  and the dashboard's embedded terminal's mirror commands.** Neither
  surface exposes `LogFilter.Attributes`/`SpanFilter.Attributes` at all
  yet - [`SearchCommand.cs`](../../src/Flare.Cli/Commands/SearchCommand.cs)'s
  own header comment already flagged this as a planned follow-up, written
  before the exists/absent/not-equals operator work (see git history)
  made it more worth doing (a plain `--attr key=value` alone couldn't
  express "missing this attribute" or "exclude this value" anyway). Needs
  a repeatable flag per operator - e.g. `--attr key=value` (Equals),
  `--attr-not key=value` (NotEquals), `--attr-exists key`/`--attr-absent
  key` - in `Flare.Cli/Commands/SearchCommand.cs`/`TracesCommand.cs` *and*
  their dashboard-terminal ports
  ([`src/dashboard/src/lib/terminal/commands/search.ts`](../../src/dashboard/src/lib/terminal/commands/search.ts)/`traces.ts`),
  which explicitly mirror the CLI's own flag set 1:1 - the two need to land
  together, not one then the other. `search.ts`'s `parseLogFilterArgs` is
  already shared with `export.ts`, so that command picks up the same flags
  for free. Not started.
- **Custom, user-built dashboards (multi-panel, saved, composed from
  arbitrary log/trace/metric queries).** A bigger item, likely needs its
  own design pass before implementation. Distinct from
  [`Saved views`](../../src/dashboard/src/routes/views) — a saved view is
  one named filter preset for a single Logs/Traces/Metrics page; a
  dashboard is a named collection of independent panels (mix of charts
  from different queries/signals) arranged on a grid. No CRUD for this
  shape exists anywhere in `Flare.Api` today. Not started.
- **Auto-refresh toggle for the Traces and Metrics explorer pages.**
  Re-runs the current query on an interval (e.g. every 30s) so the page
  stays live without a manual re-search. Logs already has live-tail via
  WebSocket, a different mechanism that doesn't cover Traces or Metrics —
  neither `$lib/traces` nor `$lib/metrics` implements interval-based
  polling today. Small, self-contained; no backend changes needed. Not
  started.
- **Exceptions/errors tracking page.** Groups recorded exceptions by
  type/message across services — occurrence count, first/last seen,
  affected services — distinct from the RED-metrics overview item above
  (that's aggregate *rate*; this is *which specific exceptions*, à la
  Sentry). The raw material already exists (`SpanRecord.Events`/
  `SpanEvent` capture OTel span events generically, which is where
  `exception.type`/`exception.message`/`exception.stacktrace` live as
  event attributes), but nothing groups/dedupes it and there's no
  `routes/errors` page. Not started.
- **Attribute-value autocomplete in the Logs/Traces filter builders.**
  Given a key (e.g. `http.route`), suggest actual observed values instead
  of requiring an exact typed match. Today only service names get this
  treatment (`loadKnownServices()` in `$lib/traces/state.svelte.ts`) — no
  general "suggest values for this attribute key" endpoint or UI exists
  for either signal. Not started.
- **Roll up "does this trace contain any error" across all its spans, not
  just the root span.** `/api/spans/search` with `RootSpansOnly` returns
  one row per trace via its root span, and `TraceRow.svelte`'s status
  badge reflects only that root span's `statusCode` — a trace with a
  healthy root span (e.g. gateway returns 200) but an erroring span deeper
  in the call chain currently shows as healthy in the trace list, only
  visible once the waterfall is opened. Needs a server-side rolled-up
  flag, not a client-side fix. Not started.
- **Notification channel improvements: PagerDuty as a fourth channel type,
  plus a "send test alert" action on any channel.**
  [`AlertModels.cs`](../../src/Flare.Api/Model/AlertModels.cs) supports
  exactly three mutually-exclusive channels today (webhook/Slack via
  `WebhookUrl`, Telegram, Email) via `ValidateChannel()` — no PagerDuty,
  and no way to verify a channel's config (URL, bot token, SMTP address)
  actually works before relying on it in a real incident. PagerDuty would
  follow the same one-channel-per-rule shape as the existing three; the
  test action is channel-agnostic and applies to all four. Not started.
