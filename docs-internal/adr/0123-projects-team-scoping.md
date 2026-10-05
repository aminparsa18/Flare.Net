# ADR-0123: Projects: per-team scoping by service allow-list

Status: Accepted (phases 1 and 2 implemented; 3-4 pending)

Date: 2026-10-05

## Context

Roles (`Admin`/`Member`/`Viewer`) are global, so one instance can't safely serve several
teams: everyone sees every service, dashboard, alert and ingest key. We want a team boundary
without re-plumbing the telemetry tables.

## Decision

- **A project is a named allow-list of `service.name` patterns**, not a tag stamped on rows.
  A pattern is an exact name or a trailing `*` prefix (`checkout-*`), the same convention as
  `LogFilter.ScopeNames`; a bare `*` is rejected so a project can't silently own everything.
  Nothing changes in ClickHouse: scope is enforced by injecting the user's allowed services
  into `LogFilter`/`SpanFilter`/metric and exception filters. The alternative, stamping a
  `ProjectId` at ingest, gives a harder boundary but needs a migration on every signal table
  and materialized view, and can't re-scope historical data.
- **Membership roles are per project and independent of `Users.Role`.** A user has a role in
  each project they belong to (`ProjectMembers`, reusing `Admin`/`Member`/`Viewer`). The
  global `Admin` stays the instance super-user and sees everything.
- **Identity tables, both providers:** `Projects`, `ProjectServicePatterns`, `ProjectMembers`
  (Sqlite migration 0028, Postgres 0003). Project names are unique case-insensitively.
- **Admin-only management API** under `/api/projects` (CRUD plus
  `/members/{userId}`), audited (ADR-0079).

## Phases

1. **Done:** schema, `IProjectStore`, pattern matcher (`ProjectServicePattern`), admin API.
   Pure data management; no query behaviour changes, so existing instances are unaffected.
2. **Done (partially, see below):** resolve the caller's allowed-service set (union of their projects' patterns; global Admin
   and instances with no projects are unrestricted) and apply it to log, span, metric and
   exception queries, live-tail and the MCP tools.
3. `ProjectId` on dashboards, alert rules, SLOs and ingest keys; list/CRUD filtered by
   membership, with project role deciding who may edit.
4. Dashboard: project switcher and a Projects admin page.

## Consequences

- A service matching no project is visible only to global Admins once phase 2 lands and any
  project exists; this is deliberate, and phase 2 must document it.
- Overlapping patterns across projects are allowed; a user's access is the union.
- Scoping is query-time, so a project that narrows its patterns takes effect immediately.

## Phase 2 as built

- `ProjectScopeMiddleware` (after authentication) resolves the caller's allow-list with the
  pure `ProjectScopeEvaluator` and stores it in `ServiceScope.Current`, an `AsyncLocal`. The
  filter SQL builders read it, so endpoints need no per-handler code. Ambient rather than
  threaded through every handler because one forgotten call site would be a data leak;
  workers and unit tests never set it and stay unrestricted.
- **Unrestricted:** global Admins, unauthenticated requests (auth disabled), and any instance
  with no projects defined (creating the first project must not blind existing users).
  A non-admin in no project, on an instance that has projects, sees nothing.
- **Enforced** in every query builder keyed by service or reading spans/logs/metrics: the
  log, span, metric and exception filter builders (so search, aggregate, facets, dashboard
  panels, alert test-fires, and the MCP/PAT paths), trace-by-id and level loading, span
  duration/rollup lookups, trace funnels and structural queries, log context, span duration
  percentiles, the active-services list, service overview/Apdex/metrics, the dependency map
  (both ends of an edge must be in scope, so an out-of-scope service's name never leaks as a
  neighbour), call breakdown, external API, messaging spans, LLM, N+1, version comparison,
  SLO status/series, error-issue evidence, the metric catalog, and live-tail (checked per
  event against the allow-list captured at subscribe time).
- **Deliberately not scoped:** infrastructure inventory that isn't keyed by service
  (Kubernetes, hosts, pods, ingestion/indexing health), and broker-level backlog gauges
  (Kafka lag, JetStream pending, RabbitMQ/SQS/Service Bus depth), which are keyed by
  destination and emitted by collectors rather than the producing/consuming services. A
  destination only becomes discoverable through scoped spans, but a caller who guesses a
  topic name can read its backlog. Closing this needs an infrastructure-level boundary and
  is out of scope for service allow-lists.
- Config objects (dashboards, alerts, SLOs, saved views, ingest keys) are still visible to
  everyone; that is phase 3.
