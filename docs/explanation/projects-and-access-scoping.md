# Projects and access scoping

Who can see which data in a shared Flare instance, and who can change what?
[Authentication model](authentication-model.md) explains how people and
machines prove who they are. This page covers what happens after that: how
projects scope telemetry and configuration, and how roles, personal access
tokens and ingest keys fit around them. To set projects up, see
[Organize teams with projects](../how-to/admin/organize-teams-with-projects.md).

## Two layers: global roles, then projects

Every user has one global role (`Viewer`, `Member` or `Admin`), and the
role decides what *kind* of action is allowed: read, edit alert rules and
dashboards, or administer users and keys. See the
[roles table](../reference/authentication-config.md).

Roles alone can't separate teams, because they are global: everyone sees every
service, dashboard and alert. **Projects** add the second layer, which decides
*which* data and objects an action applies to. With no projects defined, nothing
changes. Defining the first project switches the instance from "everyone sees
everything" to "people see what their projects allow", and global `Admin`s
remain the exception who see everything
([ADR-0123](../../docs-internal/adr/0123-projects-team-scoping.md)).

## A project is an allow-list of services, not a tag on rows

A project is a name, a list of `service.name` patterns (an exact name or a
trailing `*` prefix such as `checkout-*`) and a set of members, each with a role
*in that project*. Flare does not stamp a project id on telemetry rows. Scope is
applied at query time, by injecting the caller's allowed services into the log,
span, metric and exception filters.

This was chosen over stamping a `ProjectId` at ingest, which would give a harder
boundary but need a migration on every signal table and materialized view, and
could not re-scope data that is already stored. The consequences:

- **Changing a project takes effect immediately**, including for old data.
- **Overlapping patterns are fine.** A user's access is the union of their
  projects.
- **The scope is ambient.** It is resolved once per request and read by the
  query builders rather than passed to every handler, because one forgotten call
  site would be a data leak. Background workers, such as alert evaluation, don't
  set it and run unrestricted.
- **Instances with no projects are unrestricted.** Once any project exists, a
  service that matches no project is visible only to global Admins, and a
  non-admin who belongs to no project sees nothing, so add people to projects
  as you create the first one.
- **It reaches every read path** that is keyed by service: search, aggregation,
  facets, dashboard panels, the service map (both ends of an edge must be in
  scope, so an out-of-scope name can't leak as a neighbour), live tail, MCP
  tools, and calls made with a personal access token.

What it does **not** scope, deliberately: infrastructure inventory that isn't
keyed by service (hosts, Kubernetes, ingestion and indexing health) and
broker-level backlog gauges, which are emitted by collectors and keyed by
destination. Someone who can guess a topic name can read its backlog. Closing
that needs an infrastructure-level boundary, which service allow-lists can't
provide. If that matters to you, treat the instance, not a project, as the
isolation boundary.

## Projects also own configuration

Dashboards, saved views, alert rules, SLOs and ingest keys can each belong to one
project, or to none.

- **No project means instance-wide**, the default for everything created before
  projects existed, visible and editable as before.
- **A project-owned object is invisible to non-members.** They get a `404`, not
  a `403`, so its existence doesn't leak.
- **Writing needs a project role of `Admin` or `Member`**, and the project role
  can only *narrow* the global role. A global `Viewer` who is a project `Member`
  still can't edit.
- **Moving an object** needs write access to both the old and new project. An
  update that omits the project keeps the current one, so an older client can't
  accidentally un-scope an object by saving it.
- **Deleting a project** hides its objects from everyone but global Admins until
  one reassigns them.

Some things are shared infrastructure and are not project-scoped: notification
channels, maintenance windows, on-call rotations and pipeline rules. Alert
notifications are also not redacted by project; the rule's own project decides
who can read or edit it, and its condition was written by someone who could
query that data.

The project switcher in the dashboard is a view filter on configuration lists and
the default for new objects. It does not narrow telemetry. That is always the
union of the caller's memberships, since a second, per-request "active project"
would be another scoping mechanism to keep leak-free.

## Dashboard ownership is separate

Within a dashboard's audience, a different rule decides who may change it:
**ownership gates mutation, not visibility**
([ADR-0027](../../docs-internal/adr/0027-dashboard-ownership.md)). Everyone who
can see a dashboard still sees it, but only its creator or an Admin (in a
project, a project Admin) can rename, re-layout or delete it. A dashboard with no
recorded owner, because it predates the feature or was created while auth was
off, can be changed by any Member, to avoid a flag day that locks everyone out of
existing dashboards. The narrower option was chosen because the goal was to stop
accidental overwrites, not to make dashboards private.

## Personal access tokens: you, from a script

A personal access token (PAT) is a second credential for the *same* user, not a
new identity. It authenticates as its creator with exactly that user's role and
project memberships, so every permission and scope above applies to it unchanged
([ADR-0019](../../docs-internal/adr/0019-personal-access-tokens.md)).

- **Anyone can create one for themselves.** It's not an admin action, because a
  leaked PAT carries only its creator's existing access.
- **It rides the existing session authentication** as one more place a credential
  can be found, so every authorization rule already accepts it.
- **Disabling the user kills their tokens** at once, because the owner is
  re-checked on every request.
- **It doesn't work for live tail**, because a browser can't attach a custom
  header to the WebSocket handshake.
- **Each token is rate limited** separately from the browser session.

## Ingest keys: a machine, not a person

An ingest key authenticates an exporter, never a user, and grants no read access
at all. Because a leaked key could write anything, Flare lets an admin narrow
what a key may do, in layers:

- **Limits** cap events and bytes per minute and per day, so one noisy source can
  be throttled without revoking its key
  ([ADR-0051](../../docs-internal/adr/0051-per-ingest-key-ingestion-limits.md);
  see [Ingest reliability](ingest-reliability-and-delivery-guarantees.md)).
- **An origin allowlist** turns a key into a *browser* key: requests must carry a
  listed `Origin`. Browser keys sit in page JavaScript where any visitor can read
  them, so this limits where a leaked key works from a web page. The header is
  spoofable outside a browser, so it is not authentication
  ([ADR-0149](../../docs-internal/adr/0149-ingest-key-origin-allowlist.md)).
- **A service allowlist** refuses any request whose resources aren't one of the
  key's listed `service.name`s, so a leaked key can't pollute another service's
  logs, traces and alerts. The whole request is refused rather than the offending
  parts dropped, so a misconfigured exporter stays visible
  ([ADR-0150](../../docs-internal/adr/0150-ingest-key-service-allowlist.md)).
- **A project** records who owns the key. It is ownership only: creating, moving
  and editing a key stays a global-Admin action, because a key can otherwise write
  under any service name and a project admin could write into another project's
  services.

All of these apply only while ingest keys are required, and not to a static key.
Changes reach ingest within the key cache's refresh interval (30 seconds) rather
than instantly.

## Trade-offs

- **Query-time scoping adds a predicate to every query**, and it's only as
  complete as the list of read paths that apply it.
- **Scope is by service name**, so it is exactly as trustworthy as the
  `service.name` that exporters send. Pair it with service allowlists on ingest
  keys if teams shouldn't be able to write into each other's services.
- **It is not multi-tenant isolation.** Instance-level settings, infrastructure
  pages and shared channels stay global.

## Related

- Explanation: [Authentication model](authentication-model.md), [Ingest reliability and delivery guarantees](ingest-reliability-and-delivery-guarantees.md)
- How-to: [Organize teams with projects](../how-to/admin/organize-teams-with-projects.md), [Configure authentication](../how-to/admin/configure-authentication.md)
- Reference: [Authentication configuration](../reference/authentication-config.md)
- ADRs: [0019](../../docs-internal/adr/0019-personal-access-tokens.md), [0027](../../docs-internal/adr/0027-dashboard-ownership.md), [0051](../../docs-internal/adr/0051-per-ingest-key-ingestion-limits.md), [0123](../../docs-internal/adr/0123-projects-team-scoping.md), [0149](../../docs-internal/adr/0149-ingest-key-origin-allowlist.md), [0150](../../docs-internal/adr/0150-ingest-key-service-allowlist.md)
