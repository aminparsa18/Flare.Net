# ADR-0123: Projects: per-team scoping by service allow-list

Status: Accepted (implemented)

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
3. **Done:** `ProjectId` on dashboards, saved views, alert rules, SLOs and ingest keys;
   list/CRUD filtered by membership, with project role deciding who may edit (see
   "Phase 3 as built").
4. **Done:** dashboard project switcher, Projects admin page, project pickers on the forms
   (see "Phase 4 as built").

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
- Config objects (dashboards, alerts, SLOs, saved views, ingest keys) were still visible to
  everyone at this point; phase 3 closes that.

## Phase 3 as built

- **Storage:** a nullable `ProjectId` on `dashboards`, `saved_views`, `alert_rules` and `slos`
  (ClickHouse migration 0052, plus its cluster variant) and on Identity's `IngestApiKeys`
  (Sqlite 0029, Postgres 0004). It is part of each versioned row, so moving an object between
  projects is an ordinary update. There is no foreign key (different store); an object whose
  project was deleted is hidden from non-admins until a global Admin reassigns it.
- **Null means instance-wide**, the default for every existing row: visible to everyone and
  editable under the same rules as before (route policy, plus dashboard ownership). Nothing
  changes for an instance that doesn't assign projects.
- **Read:** a project-owned object is visible only to that project's members and to global
  Admins; to anyone else it is a 404, so existence doesn't leak. Lists are filtered, and so are
  the alert states, alert export, the SLO names an export/import resolves, and the dashboards
  the metric catalog reports as using a metric.
- **Write:** needs the project role `Admin` or `Member` (a project `Viewer` gets a 403). The
  project role can only **narrow** the global role: the route's `RequireMember` policy still
  runs first, so a global Viewer who is a project Member still can't edit. A project `Admin`
  may also change any dashboard in the project regardless of its owner (ADR-0027 ownership
  still applies to project Members). Creating or moving an object into a project needs write
  access to that project, and the project must exist (400 otherwise).
- **Update semantics:** `projectId` omitted keeps the object's current project, so a client
  that predates projects can't un-scope an object by saving it; `00000000-0000-0000-0000-000000000000`
  clears it. A move needs write access to both the old and the new project.
- **Resolved once per request** by `ProjectScopeMiddleware` into `ProjectAccess` (stashed on
  `HttpContext.Items`); handlers read it through `ProjectGuard`. Global Admins and
  auth-disabled instances are unrestricted. Unlike service scoping, an instance with no
  projects is not special-cased here, because no object can legitimately carry a `ProjectId`.
- **Ingest keys** only record ownership (`POST /api/ingest-keys` takes `projectId`,
  `PUT /api/ingest-keys/{id}/project` moves one). Managing keys stays global-Admin-only: a key
  can ingest under any `service.name`, so letting a project Admin mint keys would let them
  write into other projects' services. Opening that up needs a per-key service restriction
  first.
- **Not scoped:** alert evaluation and notification (workers run unrestricted, as designed),
  notification channels and maintenance windows (shared infrastructure), and pipeline rules.
  An alert's `{{...}}` payload isn't redacted by project; the rule's own project decides who
  can read or edit it, and its condition was authored by someone who could query that data.

## Phase 4 as built

- **`GET /api/projects/mine`** (any authenticated user) returns the projects the caller belongs
  to with their own role; global Admins and auth-disabled instances get every project as
  `Admin`. The admin `/api/projects` list stays Admin-only, so the switcher and the form
  pickers use `/mine` and work for non-admins.
- **Switcher** (`ProjectSwitcher` in the nav, hidden until the caller has a project) is a
  *view filter on config lists* (dashboards, alerts, SLOs, saved views) and the default project
  for new objects. It is stored per browser. It does not narrow telemetry queries: those are
  already scoped server-side to the union of the caller's projects, and adding a per-request
  "active project" would be a second scoping mechanism to keep leak-free. Instance-wide
  objects (`ProjectId` null) always show.
- **Projects admin page** at Settings > Workspace > Projects: CRUD, the service-pattern
  allow-list (one per line) and per-project members with roles.
- **Pickers** (`ProjectPicker`) on the dashboard, alert, SLO, saved-view and ingest-key create
  forms, listing only projects the caller can write to; hidden when there are none. A cleared
  picker on an object that had a project sends the empty GUID, since omitting `projectId`
  keeps the current one. Moving an ingest key between projects after creation has no UI yet
  (`PUT /api/ingest-keys/{id}/project` exists).
